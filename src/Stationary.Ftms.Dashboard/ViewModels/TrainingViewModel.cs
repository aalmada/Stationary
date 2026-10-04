using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.ViewModels;

public enum TrainingBrowseMode
{
    Workouts,
    Plans,
    Assessment,
}

public sealed class TrainingViewModel : ReactiveObject, IDisposable
{
    private const int CardBatchSize = 5;
    private readonly DashboardViewModel dashboard;
    private readonly CompositeDisposable disposables = [];
    private IReadOnlyDictionary<string, TrainingWorkout> workoutsById = new Dictionary<string, TrainingWorkout>(StringComparer.Ordinal);
    private readonly ObservableCollection<TrainingWorkout> workouts = [];
    private readonly ObservableCollection<TrainingPlan> visiblePlans = [];
    private IReadOnlyList<TrainingPlan> plans = [];
    private TrainingWorkout? selectedWorkout;
    private TrainingPlan? selectedPlan;
    private TrainingPlanWeek? selectedPlanWeek;
    private TrainingWorkoutRunner? runner;
    private TrainingPowerCapabilities rampCapabilities;
    private DateTimeOffset? rampStartedAt;
    private DateTimeOffset? segmentStartedAt;
    private RampFtpEstimate? rampEstimate;
    private bool isRampTest;
    private bool hasStartedRide;
    private TrainingBrowseMode browseMode = TrainingBrowseMode.Workouts;
    private string statusText = "Choose a workout and set FTP in Settings.";
    private string adherenceText = "Precision score appears after a work interval.";
    private int currentTargetWatts;
    private bool isCatalogLoading = true;
    private bool hasPublishedPlans;

    public TrainingViewModel(DashboardViewModel dashboard)
    {
        this.dashboard = dashboard;
        Workouts = new ReadOnlyObservableCollection<TrainingWorkout>(workouts);
        VisiblePlans = new ReadOnlyObservableCollection<TrainingPlan>(visiblePlans);
        LoadCatalogCommand = ReactiveCommand.CreateFromTask(LoadCatalogAsync);
        SelectWorkoutCommand = ReactiveCommand.Create<TrainingWorkout>(SelectWorkout, this.WhenAnyValue(viewModel => viewModel.IsActive).Select(static active => !active));
        SelectPlanCommand = ReactiveCommand.Create<TrainingPlan>(SelectPlan, this.WhenAnyValue(viewModel => viewModel.IsActive).Select(static active => !active));
        SelectPlanWeekCommand = ReactiveCommand.Create<TrainingPlanWeek>(SelectPlanWeek, this.WhenAnyValue(viewModel => viewModel.IsActive).Select(static active => !active));
        ShowWorkoutsCommand = ReactiveCommand.Create(() => SetBrowseMode(TrainingBrowseMode.Workouts));
        ShowPlansCommand = ReactiveCommand.CreateFromTask(ShowPlansAsync);
        ShowAssessmentCommand = ReactiveCommand.Create(() => SetBrowseMode(TrainingBrowseMode.Assessment));
        StartCommand = ReactiveCommand.CreateFromTask(StartAsync, this.WhenAnyValue(viewModel => viewModel.SelectedWorkout).Select(static workout => workout is not null));
        StartRampTestCommand = ReactiveCommand.CreateFromTask(StartRampTestAsync);
        PauseCommand = ReactiveCommand.CreateFromTask(PauseAsync, this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state == TrainingWorkoutState.Running));
        ResumeCommand = ReactiveCommand.CreateFromTask(ResumeAsync, this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state == TrainingWorkoutState.Paused));
        RetryCommand = ReactiveCommand.CreateFromTask(RetryAsync, this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state == TrainingWorkoutState.TargetFailed));
        EndCommand = ReactiveCommand.CreateFromTask(EndAsync, this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state is TrainingWorkoutState.ApplyingTarget or TrainingWorkoutState.Running or TrainingWorkoutState.Paused or TrainingWorkoutState.TargetFailed));
        FinishRampTestCommand = ReactiveCommand.CreateFromTask(FinishRampTestAsync, this.WhenAnyValue(viewModel => viewModel.IsRampTest, viewModel => viewModel.IsActive, (ramp, active) => ramp && active));
        AcceptRampEstimateCommand = ReactiveCommand.Create(AcceptRampEstimate, this.WhenAnyValue(viewModel => viewModel.RampEstimate).Select(estimate => estimate is { IsReady: true, EstimatedFtpWatts: not null }));
        disposables.Add(LoadCatalogCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                IsCatalogLoading = false;
                StatusText = "The training library could not be loaded.";
            }));
        disposables.Add(LoadCatalogCommand.Execute().Subscribe());
        disposables.Add(Observable.Interval(TimeSpan.FromSeconds(1), RxApp.TaskpoolScheduler)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => AdvanceIfDue()));
    }

    public ReadOnlyObservableCollection<TrainingWorkout> Workouts { get; }

    public IReadOnlyList<TrainingPlan> Plans
    {
        get => plans;
        private set => this.RaiseAndSetIfChanged(ref plans, value);
    }

    public ReadOnlyObservableCollection<TrainingPlan> VisiblePlans { get; }

    public bool IsCatalogLoading
    {
        get => isCatalogLoading;
        private set
        {
            if (this.RaiseAndSetIfChanged(ref isCatalogLoading, value))
            {
                this.RaisePropertyChanged(nameof(IsCatalogLoaded));
            }
        }
    }

    public bool IsCatalogLoaded => !IsCatalogLoading && Workouts.Count > 0;

    public TrainingBrowseMode BrowseMode
    {
        get => browseMode;
    }

    public bool IsWorkoutsView => BrowseMode == TrainingBrowseMode.Workouts;

    public bool IsPlansView => BrowseMode == TrainingBrowseMode.Plans;

    public bool IsAssessmentView => BrowseMode == TrainingBrowseMode.Assessment;

    public TrainingWorkout? SelectedWorkout
    {
        get => selectedWorkout;
        set => this.RaiseAndSetIfChanged(ref selectedWorkout, value);
    }

    public TrainingPlan? SelectedPlan
    {
        get => selectedPlan;
        private set => this.RaiseAndSetIfChanged(ref selectedPlan, value);
    }

    public TrainingPlanWeek? SelectedPlanWeek
    {
        get => selectedPlanWeek;
        private set
        {
            if (EqualityComparer<TrainingPlanWeek?>.Default.Equals(selectedPlanWeek, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref selectedPlanWeek, value);
            this.RaisePropertyChanged(nameof(SelectedPlanWorkouts));
            this.RaisePropertyChanged(nameof(VisiblePlanWorkouts));
        }
    }

    public IReadOnlyList<TrainingWorkout> SelectedPlanWorkouts => SelectedPlanWeek is null
        ? []
        : [.. SelectedPlanWeek.WorkoutIds.Select(workoutId => workoutsById[workoutId])];

    public IReadOnlyList<TrainingWorkout> VisiblePlanWorkouts => IsPlansView ? SelectedPlanWorkouts : [];

    public TrainingWorkoutState RunnerState => runner?.State ?? TrainingWorkoutState.Selected;

    public bool IsActive => RunnerState is TrainingWorkoutState.ApplyingTarget or TrainingWorkoutState.Running or TrainingWorkoutState.Paused or TrainingWorkoutState.TargetFailed;

    public bool HasStartedRide => hasStartedRide;

    public bool CanPause => RunnerState == TrainingWorkoutState.Running;

    public bool CanResume => RunnerState == TrainingWorkoutState.Paused;

    public bool CanRetry => RunnerState == TrainingWorkoutState.TargetFailed;

    public bool CanEnd => IsActive;

    public bool IsRampTest => isRampTest;

    public RampFtpEstimate? RampEstimate
    {
        get => rampEstimate;
        private set => this.RaiseAndSetIfChanged(ref rampEstimate, value);
    }

    public string RampEstimateText => RampEstimate switch
    {
        { Status: RampFtpEstimateStatus.Ready, EstimatedFtpWatts: { } ftp } => $"Estimated FTP: {ftp} W. Review and accept to update zones.",
        { Status: RampFtpEstimateStatus.DeviceMaximumReached } => "Ramp test was inconclusive because the bike reached its advertised power maximum.",
        { Status: RampFtpEstimateStatus.InsufficientTelemetry } => "Ramp test was inconclusive because a continuous measured minute was not recorded.",
        _ => "",
    };

    public string StatusText
    {
        get => statusText;
        private set => this.RaiseAndSetIfChanged(ref statusText, value);
    }

    public string AdherenceText
    {
        get => adherenceText;
        private set => this.RaiseAndSetIfChanged(ref adherenceText, value);
    }

    public string CurrentSegmentText => runner?.CurrentSegment is { } segment
        ? $"{segment.Definition.Name}: {segment.TargetWatts} W"
        : "No active interval";

    public string CurrentWorkoutName => IsRampTest ? "FTP ramp assessment" : SelectedWorkout?.Name ?? "Training session";

    public string IntervalProgressText => runner is { } activeRunner
        ? $"Interval {activeRunner.SegmentIndex + 1} of {activeRunner.SegmentCount}"
        : "No active interval";

    public string CurrentIntervalRemainingText => runner?.GetCurrentSegmentRemaining(DateTimeOffset.UtcNow) is { } remaining
        ? FormatRemainingTime(remaining)
        : "--:--";

    public int CurrentTargetWatts
    {
        get => currentTargetWatts;
        private set => this.RaiseAndSetIfChanged(ref currentTargetWatts, value);
    }

    public ReactiveCommand<TrainingWorkout, Unit> SelectWorkoutCommand { get; }

    public ReactiveCommand<TrainingPlan, Unit> SelectPlanCommand { get; }

    public ReactiveCommand<TrainingPlanWeek, Unit> SelectPlanWeekCommand { get; }

    public ReactiveCommand<Unit, Unit> LoadCatalogCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowWorkoutsCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowPlansCommand { get; }

    public ReactiveCommand<Unit, Unit> ShowAssessmentCommand { get; }

    public ReactiveCommand<Unit, Unit> StartCommand { get; }

    public ReactiveCommand<Unit, Unit> StartRampTestCommand { get; }

    public ReactiveCommand<Unit, Unit> PauseCommand { get; }

    public ReactiveCommand<Unit, Unit> ResumeCommand { get; }

    public ReactiveCommand<Unit, Unit> RetryCommand { get; }

    public ReactiveCommand<Unit, Unit> EndCommand { get; }

    public ReactiveCommand<Unit, Unit> FinishRampTestCommand { get; }

    public ReactiveCommand<Unit, Unit> AcceptRampEstimateCommand { get; }

    public void Dispose()
    {
        disposables.Dispose();
        LoadCatalogCommand.Dispose();
        SelectWorkoutCommand.Dispose();
        SelectPlanCommand.Dispose();
        SelectPlanWeekCommand.Dispose();
        ShowWorkoutsCommand.Dispose();
        ShowPlansCommand.Dispose();
        ShowAssessmentCommand.Dispose();
        StartCommand.Dispose();
        StartRampTestCommand.Dispose();
        PauseCommand.Dispose();
        ResumeCommand.Dispose();
        RetryCommand.Dispose();
        EndCommand.Dispose();
        FinishRampTestCommand.Dispose();
        AcceptRampEstimateCommand.Dispose();
    }

    private async Task LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var catalog = await TrainingCatalog.LoadAsync(cancellationToken);
        Plans = catalog.Plans;
        workoutsById = catalog.Workouts.ToDictionary(static workout => workout.Id, StringComparer.Ordinal);
        SelectedWorkout = catalog.Workouts.FirstOrDefault();
        SelectedPlan = Plans.FirstOrDefault();
        SelectedPlanWeek = SelectedPlan?.Weeks.FirstOrDefault();

        workouts.Clear();
        for (var workoutIndex = 0; workoutIndex < catalog.Workouts.Count; workoutIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            workouts.Add(catalog.Workouts[workoutIndex]);

            if ((workoutIndex + 1) % CardBatchSize == 0)
            {
                await Task.Yield();
            }
        }

        if (IsPlansView)
        {
            await PublishPlansAsync(cancellationToken);
        }

        IsCatalogLoading = false;
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (SelectedWorkout is null || !ushort.TryParse(dashboard.Telemetry.FunctionalThresholdPowerText, out var ftp) || ftp == 0)
        {
            StatusText = "Set a positive FTP before starting training.";
            return;
        }

        if (!dashboard.TryGetTrainingPowerCapabilities(out var capabilities))
        {
            StatusText = "This bike does not expose a usable target-power range.";
            return;
        }

        try
        {
            if (!await dashboard.StartTrainingSessionAsync(cancellationToken))
            {
                StatusText = "The bike session could not be started.";
                return;
            }

            isRampTest = false;
            await StartRunnerAsync(TrainingWorkoutCompiler.Compile(SelectedWorkout, ftp, capabilities), cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            runner = null;
            StatusText = exception.Message;
            NotifyRunnerChanged();
        }
    }

    private void SelectWorkout(TrainingWorkout workout)
    {
        SelectedWorkout = workout;
        StatusText = $"{workout.Name} selected. Set FTP before starting training.";
    }

    private void SetBrowseMode(TrainingBrowseMode mode)
    {
        if (browseMode == mode)
        {
            return;
        }

        this.RaiseAndSetIfChanged(ref browseMode, mode);
        this.RaisePropertyChanged(nameof(IsWorkoutsView));
        this.RaisePropertyChanged(nameof(IsPlansView));
        this.RaisePropertyChanged(nameof(IsAssessmentView));
        this.RaisePropertyChanged(nameof(VisiblePlanWorkouts));
    }

    private async Task ShowPlansAsync(CancellationToken cancellationToken)
    {
        SetBrowseMode(TrainingBrowseMode.Plans);

        if (!IsCatalogLoading)
        {
            await PublishPlansAsync(cancellationToken);
        }
    }

    private async Task PublishPlansAsync(CancellationToken cancellationToken)
    {
        if (hasPublishedPlans)
        {
            return;
        }

        for (var planIndex = 0; planIndex < Plans.Count; planIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            visiblePlans.Add(Plans[planIndex]);

            if ((planIndex + 1) % CardBatchSize == 0)
            {
                await Task.Yield();
            }
        }

        hasPublishedPlans = true;
    }

    private void SelectPlan(TrainingPlan plan)
    {
        SelectedPlan = plan;
        SelectedPlanWeek = plan.Weeks[0];
        StatusText = $"{plan.Name} selected. Choose a week, then one of its workouts.";
    }

    private void SelectPlanWeek(TrainingPlanWeek week)
    {
        if (SelectedPlan is null || !SelectedPlan.Weeks.Contains(week))
        {
            return;
        }

        SelectedPlanWeek = week;
        StatusText = $"Week {week.Number}: {week.Focus}";
    }

    private async Task StartRampTestAsync(CancellationToken cancellationToken)
    {
        if (!dashboard.TryGetTrainingPowerCapabilities(out rampCapabilities))
        {
            StatusText = "This bike does not expose a usable target-power range.";
            return;
        }

        if (!await dashboard.StartTrainingSessionAsync(cancellationToken))
        {
            StatusText = "The bike session could not be started.";
            return;
        }

        isRampTest = true;
        rampStartedAt = DateTimeOffset.UtcNow;
        RampEstimate = null;
        await StartRunnerAsync(RampFtpProtocol.Create(rampCapabilities), cancellationToken);
    }

    private async Task PauseAsync(CancellationToken cancellationToken)
    {
        if (!await dashboard.PauseTrainingSessionAsync(cancellationToken))
        {
            StatusText = "The bike session could not be paused.";
            return;
        }

        runner?.Pause(DateTimeOffset.UtcNow);
        StatusText = "Training timer paused.";
        NotifyRunnerChanged();
    }

    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        if (!await dashboard.StartTrainingSessionAsync(cancellationToken))
        {
            StatusText = "The bike session could not be resumed.";
            return;
        }

        runner?.Resume(DateTimeOffset.UtcNow);
        StatusText = "Training timer resumed.";
        NotifyRunnerChanged();
    }

    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        runner?.RetryTarget();
        NotifyRunnerChanged();
        await ApplyCurrentTargetAsync(cancellationToken);
    }

    private async Task EndAsync(CancellationToken cancellationToken)
    {
        if (!await dashboard.EndTrainingSessionAsync(cancellationToken))
        {
            StatusText = "The bike did not confirm the end of this training session.";
            return;
        }

        runner?.Abort();
        isRampTest = false;
        StatusText = "Training ended.";
        NotifyRunnerChanged();
        dashboard.RequestNavigation(DashboardDestination.Results);
    }

    private async Task FinishRampTestAsync(CancellationToken cancellationToken)
    {
        if (!isRampTest || rampStartedAt is not { } startedAt)
        {
            return;
        }

        if (!await dashboard.PauseTrainingSessionAsync(cancellationToken))
        {
            StatusText = "The bike session could not be paused.";
            return;
        }

        runner?.Abort();
        RampEstimate = RampFtpEstimator.Estimate(dashboard.GetRecordedSamplesSince(startedAt), rampCapabilities.MaximumWatts);
        isRampTest = false;
        StatusText = RampEstimateText;
        NotifyRunnerChanged();
    }

    private void AcceptRampEstimate()
    {
        if (RampEstimate is not { IsReady: true, EstimatedFtpWatts: { } ftp })
        {
            return;
        }

        dashboard.Telemetry.FunctionalThresholdPowerText = ftp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        dashboard.Telemetry.ApplyFunctionalThresholdPowerCommand.Execute();
        StatusText = $"FTP updated to {ftp} W.";
    }

    private void AdvanceIfDue()
    {
        var now = DateTimeOffset.UtcNow;
        if (runner is not null)
        {
            this.RaisePropertyChanged(nameof(CurrentIntervalRemainingText));
        }

        var completedSegment = runner?.CurrentSegment;
        if (runner?.Advance(now) != true)
        {
            return;
        }

        RecordAdherence(completedSegment, now);
        NotifyRunnerChanged();
        if (runner.State == TrainingWorkoutState.Completed)
        {
            _ = CompleteWorkoutAsync();
            return;
        }

        _ = ApplyCurrentTargetAsync(CancellationToken.None);
    }

    private async Task ApplyCurrentTargetAsync(CancellationToken cancellationToken)
    {
        if (runner?.CurrentSegment is not { } segment)
        {
            return;
        }

        CurrentTargetWatts = segment.TargetWatts;
        StatusText = $"Applying {segment.TargetWatts} W for {segment.Definition.Name}.";
        if (await dashboard.ApplyTrainingTargetAsync(segment.TargetWatts, cancellationToken))
        {
            segmentStartedAt = DateTimeOffset.UtcNow;
            runner.ConfirmTargetApplied(segmentStartedAt.Value);
            hasStartedRide = true;
            StatusText = segment.Definition.Cue;
        }
        else
        {
            runner.RejectTarget();
            StatusText = "The bike did not accept this target. Retry or end training.";
        }

        NotifyRunnerChanged();
    }

    private async Task CompleteWorkoutAsync()
    {
        if (await dashboard.EndTrainingSessionAsync(CancellationToken.None))
        {
            StatusText = "Workout complete.";
            dashboard.RequestNavigation(DashboardDestination.Results);
            return;
        }

        StatusText = "The final interval completed, but the bike did not confirm the session end.";
    }

    private async Task StartRunnerAsync(CompiledTrainingWorkout workout, CancellationToken cancellationToken)
    {
        hasStartedRide = false;
        runner = new(workout);
        runner.Start();
        NotifyRunnerChanged();
        await ApplyCurrentTargetAsync(cancellationToken);
    }

    private void NotifyRunnerChanged()
    {
        this.RaisePropertyChanged(nameof(RunnerState));
        this.RaisePropertyChanged(nameof(IsActive));
        this.RaisePropertyChanged(nameof(HasStartedRide));
        this.RaisePropertyChanged(nameof(CanPause));
        this.RaisePropertyChanged(nameof(CanResume));
        this.RaisePropertyChanged(nameof(CanRetry));
        this.RaisePropertyChanged(nameof(CanEnd));
        this.RaisePropertyChanged(nameof(IsRampTest));
        this.RaisePropertyChanged(nameof(CurrentWorkoutName));
        this.RaisePropertyChanged(nameof(IntervalProgressText));
        this.RaisePropertyChanged(nameof(CurrentSegmentText));
        this.RaisePropertyChanged(nameof(CurrentIntervalRemainingText));
        this.RaisePropertyChanged(nameof(CurrentTargetWatts));
        this.RaisePropertyChanged(nameof(RampEstimateText));
    }

    private static string FormatRemainingTime(TimeSpan remaining)
    {
        var seconds = (int)double.Ceiling(remaining.TotalSeconds);
        return $"{seconds / 60:D2}:{seconds % 60:D2}";
    }

    private void RecordAdherence(CompiledTrainingSegment? segment, DateTimeOffset completedAt)
    {
        if (segment is not { Definition.CountsTowardAdherence: true } || segmentStartedAt is not { } startedAt || !dashboard.TryGetTrainingPowerCapabilities(out var capabilities))
        {
            return;
        }

        var samples = dashboard.GetRecordedSamplesSince(startedAt)
            .Select(static sample => new TrainingAdherenceSample(sample.CapturedAt, sample.PowerWatts))
            .ToArray();
        var result = TrainingAdherence.Calculate(samples, segment.TargetWatts, capabilities.IncrementWatts, startedAt, completedAt, completed: true);
        AdherenceText = result.Percentage is double percentage
            ? $"Precision: {percentage:F0}% · {result.Stars} star{(result.Stars == 1 ? string.Empty : "s")}"
            : "Precision unavailable: no measured power was recorded.";
    }
}