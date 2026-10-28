using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.ViewModels;

public enum TrainingBrowseMode
{
    Workouts,
    Plans,
    Assessment,
}

public sealed partial class TrainingViewModel : ReactiveObject, IDisposable
{
    private const int CardBatchSize = 5;
    private readonly DashboardViewModel dashboard;
    private readonly CompositeDisposable disposables = [];
    private readonly IObservable<bool> canPause;
    private readonly IObservable<bool> canSelect;
    private readonly IObservable<bool> canStart;
    private readonly IObservable<bool> canStartRampTest;
    private readonly IObservable<bool> canResume;
    private readonly IObservable<bool> canRetry;
    private readonly IObservable<bool> canEnd;
    private readonly IObservable<bool> canFinishRampTest;
    private readonly IObservable<bool> canAcceptRampEstimate;
    private IReadOnlyDictionary<string, TrainingWorkout> workoutsById = new Dictionary<string, TrainingWorkout>(StringComparer.Ordinal);
    private readonly ObservableCollection<TrainingWorkout> workouts = [];
    private readonly ObservableCollection<TrainingPlan> visiblePlans = [];
    private TrainingPlanWeek? selectedPlanWeek;
    private TrainingWorkoutRunner? runner;
    private CompiledTrainingWorkout? activeWorkout;
    private IReadOnlyList<TrainingSegment> activeSegments = [];
    private IReadOnlyList<TelemetrySample> targetPowerSamples = [];
    private bool isSessionStartPending;
    private TrainingPowerCapabilities rampCapabilities;
    private DateTimeOffset? rampStartedAt;
    private DateTimeOffset? segmentStartedAt;
    private bool isRampTest;
    private bool isPausePending;
    private bool isEnding;
    private bool isEndFailed;
    private bool isLocallyEndable;
    private bool hasSessionStarted;
    private long sessionGeneration;
    private Task<bool>? sessionStartTask;
    private TrainingBrowseMode browseMode = TrainingBrowseMode.Workouts;
    private bool isCatalogLoading = true;
    private bool hasPublishedPlans;

    public TrainingViewModel(DashboardViewModel dashboard)
    {
        this.dashboard = dashboard;
        Workouts = new ReadOnlyObservableCollection<TrainingWorkout>(workouts);
        VisiblePlans = new ReadOnlyObservableCollection<TrainingPlan>(visiblePlans);
        canSelect = this.WhenAnyValue(viewModel => viewModel.IsActive).Select(static active => !active);
        var hasPowerTarget = dashboard.WhenAnyValue(
                viewModel => viewModel.IsConnected,
                viewModel => viewModel.PowerTargetControl,
                static (connected, power) => connected && power is not null);
        var hasFunctionalThresholdPower = dashboard.WhenAnyValue(viewModel => viewModel.Telemetry.FunctionalThresholdPowerText)
            .Select(static text => ushort.TryParse(text, out var watts) && watts > 0);
        canStart = Observable.CombineLatest(
                    this.WhenAnyValue(viewModel => viewModel.SelectedWorkout).Select(static workout => workout is not null),
                    hasPowerTarget,
                    hasFunctionalThresholdPower,
                    canSelect,
                    static (selected, power, ftp, idle) => selected && power && ftp && idle)
                .ObserveOn(RxApp.MainThreadScheduler);
        disposables.Add(StartCommand.IsExecuting
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(value => IsStarting = value));
        disposables.Add(StartCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(exception => StatusText = exception is OperationCanceledException
                ? "Starting training was cancelled."
                : $"Training could not start: {exception.Message}"));
        canStartRampTest = Observable.CombineLatest(hasPowerTarget, canSelect, static (power, idle) => power && idle).ObserveOn(RxApp.MainThreadScheduler);
        disposables.Add(StartRampTestCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(exception => StatusText = $"The ramp test could not start: {exception.Message}"));
        canPause = this.WhenAnyValue(viewModel => viewModel.CanPause);
        canResume = this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state == TrainingWorkoutState.Paused);
        canRetry = this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state == TrainingWorkoutState.TargetFailed);
        canEnd = this.WhenAnyValue(viewModel => viewModel.CanEnd);
        canFinishRampTest = this.WhenAnyValue(viewModel => viewModel.CanFinishRampTest);
        canAcceptRampEstimate = this.WhenAnyValue(viewModel => viewModel.RampEstimate).Select(static estimate => estimate is RampFtpEstimateReady);
        disposables.Add(Observable.Merge(
            PauseCommand.ThrownExceptions,
            ResumeCommand.ThrownExceptions,
            RetryCommand.ThrownExceptions,
            EndCommand.ThrownExceptions,
            FinishRampTestCommand.ThrownExceptions,
            AcceptRampEstimateCommand.ThrownExceptions)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(exception => StatusText = $"Training operation failed: {exception.Message}"));
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
        disposables.Add(Observable.CombineLatest(
            dashboard.WhenAnyValue(viewModel => viewModel.IsConnected),
            dashboard.WhenAnyValue(viewModel => viewModel.SessionState),
            (connected, state) => (connected, state))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(state => HandleDashboardSessionState(state.connected, state.state)));
    }

    public ReadOnlyObservableCollection<TrainingWorkout> Workouts { get; }

    public DashboardViewModel Dashboard => dashboard;

    [Reactive]
    public partial IReadOnlyList<TrainingPlan> Plans { get; private set; } = [];

    public ReadOnlyObservableCollection<TrainingPlan> VisiblePlans { get; }

    public bool IsCatalogLoading
    {
        get => isCatalogLoading;
        private set
        {
            if (isCatalogLoading != value)
            {
                this.RaiseAndSetIfChanged(ref isCatalogLoading, value);
                this.RaisePropertyChanged(nameof(IsCatalogLoaded));
            }
        }
    }

    public bool IsCatalogLoaded => !IsCatalogLoading && Workouts.Count > 0;

    [Reactive]
    public partial bool IsStarting { get; private set; }

    public TrainingBrowseMode BrowseMode
    {
        get => browseMode;
    }

    public bool IsWorkoutsView => BrowseMode == TrainingBrowseMode.Workouts;

    public bool IsPlansView => BrowseMode == TrainingBrowseMode.Plans;

    public bool IsAssessmentView => BrowseMode == TrainingBrowseMode.Assessment;
    public string WorkoutsTabAccessibleName => IsWorkoutsView ? "Workouts, selected" : "Workouts";

    public string PlansTabAccessibleName => IsPlansView ? "Plans, selected" : "Plans";

    public string AssessmentTabAccessibleName => IsAssessmentView ? "Assessment, selected" : "Assessment";

    [Reactive]
    public partial TrainingWorkout? SelectedWorkout { get; set; }

    [Reactive]
    public partial TrainingPlan? SelectedPlan { get; private set; }

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
    public bool IsActive => RunnerState is TrainingWorkoutState.ApplyingTarget or TrainingWorkoutState.Running or TrainingWorkoutState.Paused or TrainingWorkoutState.TargetFailed
        || isEnding
        || isEndFailed
        || isLocallyEndable;

    public IReadOnlyList<TrainingSegment> ActiveSegments => activeSegments;

    public IReadOnlyList<TelemetrySample> TargetPowerSamples
    {
        get => IsActive ? targetPowerSamples : [];
        private set => this.RaiseAndSetIfChanged(ref targetPowerSamples, value);
    }

    public double WorkoutProgress => GetWorkoutTotal() is { Ticks: > 0 } total
        ? double.Clamp(GetWorkoutElapsed(DateTimeOffset.UtcNow) / total, 0d, 1d)
        : 0d;

    public string WorkoutElapsedText => FormatRemainingTime(GetWorkoutElapsed(DateTimeOffset.UtcNow));

    public string WorkoutRemainingText => runner is null
        ? "--:--"
        : FormatRemainingTime(GetWorkoutTotal() - GetWorkoutElapsed(DateTimeOffset.UtcNow));

    public string CurrentTargetText => IsActive && CurrentTargetWatts > 0
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"ERG target {CurrentTargetWatts} W")
        : string.Empty;

    public double? PowerCadenceTargetWatts => IsActive && CurrentTargetWatts > 0 ? CurrentTargetWatts : null;

    public int? PowerCadenceMinimumCadenceRpm => IsActive ? runner?.CurrentSegment?.Definition.MinimumCadenceRpm : null;

    public int? PowerCadenceMaximumCadenceRpm => IsActive ? runner?.CurrentSegment?.Definition.MaximumCadenceRpm : null;

    public string CadenceTargetText => runner?.CurrentSegment?.Definition switch
    {
        { MinimumCadenceRpm: { } minimum, MaximumCadenceRpm: { } maximum } => $"Target {minimum}-{maximum} rpm",
        { MinimumCadenceRpm: { } minimum } => $"Target {minimum}+ rpm",
        { MaximumCadenceRpm: { } maximum } => $"Target up to {maximum} rpm",
        _ => string.Empty,
    };

    public string NextSegmentText => runner is { } activeRunner && activeWorkout is { } workout
        ? activeRunner.SegmentIndex + 1 < workout.Segments.Count
            ? $"Next: {workout.Segments[activeRunner.SegmentIndex + 1].Definition.Name} · {FormatRemainingTime(workout.Segments[activeRunner.SegmentIndex + 1].Definition.Duration)} at {workout.Segments[activeRunner.SegmentIndex + 1].TargetWatts} W"
            : "Final interval"
        : string.Empty;

    public bool CanPause => RunnerState == TrainingWorkoutState.Running && !isPausePending;

    public bool CanResume => RunnerState == TrainingWorkoutState.Paused;

    public bool CanRetry => RunnerState == TrainingWorkoutState.TargetFailed;

    public bool CanEnd => IsActive;

    public bool CanFinishRampTest => IsRampTest && hasSessionStarted && !isSessionStartPending && IsActive && !isEnding;

    public bool IsRampTest => isRampTest;

    [Reactive]
    public partial RampFtpEstimate? RampEstimate { get; private set; }

    public string RampEstimateText => RampEstimate switch
    {
        RampFtpEstimateReady estimate => $"Estimated FTP: {estimate.EstimatedFtpWatts} W. Review and accept to update zones.",
        RampFtpEstimateDeviceMaximumReached => "Ramp test was inconclusive because the bike reached its advertised power maximum.",
        RampFtpEstimateInsufficientTelemetry => "Ramp test was inconclusive because a continuous measured minute was not recorded.",
        null => "",
    };

    [Reactive]
    public partial string StatusText { get; private set; } = "Choose a workout.";

    [Reactive]
    public partial string AdherenceText { get; private set; } = "Cadence in-range score appears after a work interval.";

    public string CurrentSegmentText => runner?.CurrentSegment is { } segment
        ? $"{segment.Definition.Name}: {segment.TargetWatts} W"
        : "No active interval";

    public string CurrentWorkoutName => IsRampTest ? "FTP ramp test" : activeWorkout?.Definition.Name ?? SelectedWorkout?.Name ?? "Workout";

    public string IntervalProgressText => runner is { } activeRunner
        ? $"Interval {activeRunner.SegmentIndex + 1} of {activeRunner.SegmentCount}"
        : "No active interval";

    public string CurrentIntervalRemainingText => runner?.GetCurrentSegmentRemaining(DateTimeOffset.UtcNow) is { } remaining
        ? FormatRemainingTime(remaining)
        : "--:--";

    public string IntervalGuidanceText
    {
        get
        {
            if (runner?.CurrentSegment is not { } segment)
            {
                return string.Empty;
            }

            if (runner.State == TrainingWorkoutState.ApplyingTarget)
            {
                return "Applying ERG target...";
            }

            if (runner.State == TrainingWorkoutState.Paused)
            {
                return "Training paused.";
            }

            if (runner.State == TrainingWorkoutState.Running
                && runner.GetCurrentSegmentRemaining(DateTimeOffset.UtcNow) is { } remaining
                && segment.Definition.Duration - remaining < TrainingAdherence.TrainerSettlingDuration)
            {
                return "Keep cadence steady while the trainer adjusts resistance.";
            }

            return segment.Definition.Cue;
        }
    }

    [Reactive]
    public partial int CurrentTargetWatts { get; private set; }

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
        ShowHomeCommand.Dispose();
        StartCommand.Dispose();
        StartRampTestCommand.Dispose();
        PauseCommand.Dispose();
        ResumeCommand.Dispose();
        RetryCommand.Dispose();
        EndCommand.Dispose();
        FinishRampTestCommand.Dispose();
        AcceptRampEstimateCommand.Dispose();
    }

    [ReactiveCommand]
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

    [ReactiveCommand(CanExecute = nameof(canStart))]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        var workout = SelectedWorkout;
        if (workout is null)
        {
            StatusText = "Choose a workout before starting training.";
            return;
        }

        if (!ushort.TryParse(dashboard.Telemetry.FunctionalThresholdPowerText, out var ftp) || ftp == 0)
        {
            StatusText = "Enter a positive FTP in Settings before starting training.";
            return;
        }

        if (!dashboard.TryGetTrainingPowerCapabilities(out var capabilities))
        {
            StatusText = "Connect a bike with a supported power-target range before starting.";
            return;
        }

        try
        {
            var compiledWorkout = TrainingWorkoutCompiler.Compile(
                workout,
                ftp,
                capabilities,
                dashboard.Telemetry.CadenceTargetOffsetRpm);
            isRampTest = false;
            await BeginAsync(compiledWorkout, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            runner = null;
            StatusText = exception.Message;
            NotifyRunnerChanged();
        }
    }

    [ReactiveCommand(CanExecute = nameof(canSelect))]
    private void SelectWorkout(TrainingWorkout workout)
    {
        SelectedWorkout = workout;
        StatusText = $"{workout.Name} selected.";
    }

    [ReactiveCommand]
    private void ShowWorkouts() => SetBrowseMode(TrainingBrowseMode.Workouts);

    [ReactiveCommand]
    private void ShowAssessment() => SetBrowseMode(TrainingBrowseMode.Assessment);

    [ReactiveCommand]
    private void ShowHome() => dashboard.RequestNavigation(DashboardDestination.Today);

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
        this.RaisePropertyChanged(nameof(WorkoutsTabAccessibleName));
        this.RaisePropertyChanged(nameof(PlansTabAccessibleName));
        this.RaisePropertyChanged(nameof(AssessmentTabAccessibleName));
        this.RaisePropertyChanged(nameof(VisiblePlanWorkouts));
    }

    [ReactiveCommand]
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

    [ReactiveCommand(CanExecute = nameof(canSelect))]
    private void SelectPlan(TrainingPlan plan)
    {
        SelectedPlan = plan;
        SelectedPlanWeek = plan.Weeks[0];
        StatusText = $"{plan.Name} selected. Choose a week, then one of its workouts.";
    }

    [ReactiveCommand(CanExecute = nameof(canSelect))]
    private void SelectPlanWeek(TrainingPlanWeek week)
    {
        if (SelectedPlan is null || !SelectedPlan.Weeks.Contains(week))
        {
            return;
        }

        SelectedPlanWeek = week;
        StatusText = $"Week {week.Number}: {week.Focus}";
    }

    [ReactiveCommand(CanExecute = nameof(canStartRampTest))]
    private async Task StartRampTestAsync(CancellationToken cancellationToken)
    {
        if (!dashboard.TryGetTrainingPowerCapabilities(out rampCapabilities))
        {
            StatusText = "This bike does not expose a usable target-power range.";
            return;
        }

        isRampTest = true;
        rampStartedAt = DateTimeOffset.UtcNow;
        RampEstimate = null;
        await BeginAsync(RampFtpProtocol.Create(rampCapabilities), cancellationToken);
    }

    [ReactiveCommand(CanExecute = nameof(canPause))]
    private async Task PauseAsync(CancellationToken cancellationToken)
    {
        if (isPausePending || runner?.State != TrainingWorkoutState.Running)
        {
            return;
        }

        isPausePending = true;
        NotifyRunnerChanged();
        try
        {
            if (!await dashboard.PauseTrainingSessionAsync(cancellationToken))
            {
                StatusText = "The bike session could not be paused.";
                return;
            }

            runner?.Pause(DateTimeOffset.UtcNow);
            StatusText = "Training timer paused.";
        }
        finally
        {
            isPausePending = false;
            NotifyRunnerChanged();
        }
    }

    [ReactiveCommand(CanExecute = nameof(canResume))]
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

    [ReactiveCommand(CanExecute = nameof(canRetry))]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        var generation = sessionGeneration;
        runner?.RetryTarget();
        NotifyRunnerChanged();
        if (isSessionStartPending)
        {
            var startTask = TryStartSessionAsync(cancellationToken, generation);
            sessionStartTask = startTask;
            if (!await startTask || generation != sessionGeneration)
            {
                return;
            }

            if (ReferenceEquals(sessionStartTask, startTask))
            {
                sessionStartTask = null;
            }
        }

        await ApplyCurrentTargetAsync(cancellationToken, generation);
    }

    [ReactiveCommand(CanExecute = nameof(canEnd))]
    private async Task EndAsync(CancellationToken cancellationToken)
    {
        if (isEnding)
        {
            return;
        }

        var hadStarted = hasSessionStarted;
        var pendingStart = isSessionStartPending && dashboard.IsConnected ? sessionStartTask : null;
        isEnding = true;
        isEndFailed = false;
        isLocallyEndable = false;
        sessionGeneration++;
        StatusText = "Ending training and waiting for the bike...";
        NotifyRunnerChanged();

        var startAccepted = false;
        if (pendingStart is not null)
        {
            try
            {
                startAccepted = await pendingStart;
            }
            catch (OperationCanceledException exception)
            {
                startAccepted = dashboard.SessionState is DashboardViewModel.WorkoutSessionState.Running or DashboardViewModel.WorkoutSessionState.Paused;
                StatusText = $"The start response was canceled while ending training: {exception.Message}";
            }
            catch (Exception exception)
            {
                startAccepted = dashboard.SessionState is DashboardViewModel.WorkoutSessionState.Running or DashboardViewModel.WorkoutSessionState.Paused;
                StatusText = $"The start response failed while ending training: {exception.Message}";
            }
        }

        hadStarted |= startAccepted;
        var needsMachineStop = dashboard.IsConnected
            && dashboard.SessionState is (DashboardViewModel.WorkoutSessionState.Running or DashboardViewModel.WorkoutSessionState.Paused);
        if (needsMachineStop && !await dashboard.EndTrainingSessionAsync(CancellationToken.None))
        {
            isEnding = false;
            isEndFailed = true;
            StatusText = "The bike did not confirm the end. Retry ending training before starting another ride.";
            NotifyRunnerChanged();
            return;
        }

        isSessionStartPending = false;
        sessionStartTask = null;
        runner?.Abort();
        isRampTest = false;
        isEnding = false;
        isEndFailed = false;
        isLocallyEndable = false;
        hasSessionStarted = false;
        StatusText = "Training ended.";
        NotifyRunnerChanged();
        dashboard.RequestNavigation(hadStarted ? DashboardDestination.Results : DashboardDestination.Today);
    }

    [ReactiveCommand(CanExecute = nameof(canFinishRampTest))]
    private async Task FinishRampTestAsync(CancellationToken cancellationToken)
    {
        await FinalizeRampTestAsync(cancellationToken, allStepsExhausted: false);
    }

    [ReactiveCommand(CanExecute = nameof(canAcceptRampEstimate))]
    private async Task AcceptRampEstimateAsync(CancellationToken cancellationToken)
    {
        if (RampEstimate is not RampFtpEstimateReady estimate)
        {
            return;
        }

        var ftp = estimate.EstimatedFtpWatts;
        dashboard.Telemetry.FunctionalThresholdPowerText = ftp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await dashboard.Telemetry.ApplyFunctionalThresholdPowerCommand.Execute().ToTask(cancellationToken);
        StatusText = $"FTP updated to {ftp} W.";
    }

    private void AdvanceIfDue()
    {
        if (isPausePending || isEnding || isEndFailed || dashboard.SessionState != DashboardViewModel.WorkoutSessionState.Running)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (runner is not null)
        {
            NotifyClockChanged();
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
            _ = isRampTest
                ? FinalizeRampTestAsync(CancellationToken.None, allStepsExhausted: true)
                : CompleteWorkoutAsync();
            return;
        }

        _ = ApplyCurrentTargetAsync(CancellationToken.None, sessionGeneration);
    }

    private async Task ApplyCurrentTargetAsync(CancellationToken cancellationToken, long generation)
    {
        if (generation != sessionGeneration || runner?.CurrentSegment is not { } segment)
        {
            return;
        }

        CurrentTargetWatts = segment.TargetWatts;
        StatusText = $"Applying {segment.TargetWatts} W for {segment.Definition.Name}.";
        if (await dashboard.ApplyTrainingTargetAsync(segment.TargetWatts, cancellationToken))
        {
            if (generation != sessionGeneration)
            {
                return;
            }

            segmentStartedAt = DateTimeOffset.UtcNow;
            runner.ConfirmTargetApplied(segmentStartedAt.Value);
            if (dashboard.SessionState == DashboardViewModel.WorkoutSessionState.Paused)
            {
                runner.Pause(segmentStartedAt.Value);
            }

            TargetPowerSamples = [.. TargetPowerSamples, new TelemetrySample(segmentStartedAt.Value, segment.TargetWatts)];
            StatusText = segment.Definition.Cue;
        }
        else
        {
            if (generation != sessionGeneration)
            {
                return;
            }

            runner.RejectTarget();
            StatusText = "The bike did not accept this target. Retry or end training.";
        }

        NotifyRunnerChanged();
    }

    private async Task CompleteWorkoutAsync()
    {
        if (isRampTest)
        {
            await FinalizeRampTestAsync(CancellationToken.None, allStepsExhausted: true);
            return;
        }

        isEnding = true;
        isEndFailed = false;
        StatusText = "Workout complete. Waiting for the bike to stop...";
        NotifyRunnerChanged();
        if (!await TryEndMachineSessionAsync(CancellationToken.None))
        {
            isEnding = false;
            isEndFailed = true;
            StatusText = "The final interval completed, but the bike did not confirm the session end. Retry ending training.";
            NotifyRunnerChanged();
            return;
        }

        isEnding = false;
        isEndFailed = false;
        isLocallyEndable = false;
        hasSessionStarted = false;
        StatusText = "Workout complete.";
        NotifyRunnerChanged();
        dashboard.RequestNavigation(DashboardDestination.Results);
    }

    private async Task BeginAsync(CompiledTrainingWorkout workout, CancellationToken cancellationToken)
    {
        activeWorkout = workout;
        var generation = ++sessionGeneration;
        activeSegments = [.. workout.Segments.Select(static segment => segment.Definition)];
        TargetPowerSamples = [];
        CurrentTargetWatts = 0;
        AdherenceText = "Cadence in-range score appears after a work interval.";
        isSessionStartPending = true;
        isEnding = false;
        isEndFailed = false;
        isLocallyEndable = false;
        hasSessionStarted = false;
        runner = new(workout);
        runner.Start();
        this.RaisePropertyChanged(nameof(ActiveSegments));
        NotifyRunnerChanged();
        dashboard.RequestNavigation(DashboardDestination.Ride);
        var startTask = TryStartSessionAsync(cancellationToken, generation);
        sessionStartTask = startTask;
        try
        {
            if (await startTask && generation == sessionGeneration)
            {
                await ApplyCurrentTargetAsync(cancellationToken, generation);
            }
        }
        finally
        {
            if (ReferenceEquals(sessionStartTask, startTask))
            {
                sessionStartTask = null;
            }
        }
    }

    private async Task<bool> TryStartSessionAsync(CancellationToken cancellationToken, long generation)
    {
        StatusText = "Requesting control and starting the bike session...";
        var started = await dashboard.StartTrainingSessionAsync(cancellationToken);
        if (started)
        {
            hasSessionStarted = true;
        }

        if (generation != sessionGeneration)
        {
            return started;
        }

        if (started)
        {
            isSessionStartPending = false;
            return true;
        }

        runner?.RejectTarget();
        StatusText = $"The bike did not confirm the session start. Retry or end. {dashboard.ConnectionStatus}";
        NotifyRunnerChanged();
        return false;
    }

    private async Task FinalizeRampTestAsync(CancellationToken cancellationToken, bool allStepsExhausted)
    {
        if (!isRampTest || rampStartedAt is not { } startedAt || isEnding)
        {
            return;
        }

        isEnding = true;
        isEndFailed = false;
        isLocallyEndable = false;
        sessionGeneration++;
        StatusText = "Ending the ramp test and waiting for the bike...";
        NotifyRunnerChanged();
        if (!await TryEndMachineSessionAsync(cancellationToken))
        {
            isEnding = false;
            isEndFailed = true;
            StatusText = "The bike did not confirm the ramp-test end. Retry ending training.";
            NotifyRunnerChanged();
            return;
        }

        runner?.Abort();
        RampEstimate = allStepsExhausted
            ? new RampFtpEstimateDeviceMaximumReached()
            : RampFtpEstimator.Estimate(dashboard.GetRecordedSamplesSince(startedAt), rampCapabilities.MaximumWatts);
        isRampTest = false;
        isEnding = false;
        isEndFailed = false;
        isLocallyEndable = false;
        hasSessionStarted = false;
        StatusText = RampEstimateText;
        NotifyRunnerChanged();
        dashboard.RequestNavigation(DashboardDestination.Training);
    }

    private async Task<bool> TryEndMachineSessionAsync(CancellationToken cancellationToken)
    {
        if (!dashboard.IsConnected
            || dashboard.SessionState is not (DashboardViewModel.WorkoutSessionState.Running or DashboardViewModel.WorkoutSessionState.Paused))
        {
            return true;
        }

        var ended = await dashboard.EndTrainingSessionAsync(cancellationToken);
        return ended
            || !dashboard.IsConnected
            || dashboard.SessionState == DashboardViewModel.WorkoutSessionState.Stopped;
    }

    private void HandleDashboardSessionState(bool connected, DashboardViewModel.WorkoutSessionState state)
    {
        if (runner is null || !IsActive || isEnding)
        {
            return;
        }

        if (!connected)
        {
            sessionGeneration++;
            isSessionStartPending = false;
            sessionStartTask = null;
            runner.Abort();
            isRampTest = false;
            isEndFailed = false;
            isLocallyEndable = true;
            StatusText = "Connection lost. End training locally to leave the ride.";
            NotifyRunnerChanged();
            return;
        }

        if (state == DashboardViewModel.WorkoutSessionState.Paused && runner.State == TrainingWorkoutState.Running)
        {
            runner.Pause(DateTimeOffset.UtcNow);
            StatusText = "The bike paused. Training timer paused.";
            NotifyRunnerChanged();
        }
        else if (state == DashboardViewModel.WorkoutSessionState.Running && runner.State == TrainingWorkoutState.Paused)
        {
            runner.Resume(DateTimeOffset.UtcNow);
            StatusText = "The bike resumed. Training timer resumed.";
            NotifyRunnerChanged();
        }
        else if (state == DashboardViewModel.WorkoutSessionState.Stopped
            && runner.State is not (TrainingWorkoutState.Completed or TrainingWorkoutState.Aborted))
        {
            sessionGeneration++;
            isSessionStartPending = false;
            sessionStartTask = null;
            runner.Abort();
            isRampTest = false;
            isEndFailed = false;
            isLocallyEndable = true;
            StatusText = "The bike stopped the session. End training locally to leave the ride.";
            NotifyRunnerChanged();
        }
    }

    private TimeSpan GetWorkoutTotal()
    {
        var total = TimeSpan.Zero;
        foreach (var segment in activeSegments)
        {
            total += segment.Duration;
        }

        return total;
    }

    private TimeSpan GetWorkoutElapsed(DateTimeOffset timestamp)
    {
        if (runner is null)
        {
            return TimeSpan.Zero;
        }

        var elapsed = TimeSpan.Zero;
        var completedCount = int.Min(runner.SegmentIndex, activeSegments.Count);
        for (var index = 0; index < completedCount; index++)
        {
            elapsed += activeSegments[index].Duration;
        }

        if (runner.CurrentSegment is { } segment && runner.GetCurrentSegmentRemaining(timestamp) is { } remaining)
        {
            elapsed += segment.Definition.Duration - remaining;
        }

        return elapsed;
    }

    private void NotifyClockChanged()
    {
        this.RaisePropertyChanged(nameof(CurrentIntervalRemainingText));
        this.RaisePropertyChanged(nameof(IntervalGuidanceText));
        this.RaisePropertyChanged(nameof(WorkoutProgress));
        this.RaisePropertyChanged(nameof(WorkoutElapsedText));
        this.RaisePropertyChanged(nameof(WorkoutRemainingText));
    }

    private void NotifyRunnerChanged()
    {
        this.RaisePropertyChanged(nameof(RunnerState));
        this.RaisePropertyChanged(nameof(IsActive));
        this.RaisePropertyChanged(nameof(TargetPowerSamples));
        this.RaisePropertyChanged(nameof(CanPause));
        this.RaisePropertyChanged(nameof(CanResume));
        this.RaisePropertyChanged(nameof(CanRetry));
        this.RaisePropertyChanged(nameof(CanEnd));
        this.RaisePropertyChanged(nameof(CanFinishRampTest));
        this.RaisePropertyChanged(nameof(IsRampTest));
        this.RaisePropertyChanged(nameof(CurrentWorkoutName));
        this.RaisePropertyChanged(nameof(IntervalProgressText));
        this.RaisePropertyChanged(nameof(CurrentSegmentText));
        this.RaisePropertyChanged(nameof(CurrentTargetWatts));
        this.RaisePropertyChanged(nameof(CurrentTargetText));
        this.RaisePropertyChanged(nameof(PowerCadenceTargetWatts));
        this.RaisePropertyChanged(nameof(PowerCadenceMinimumCadenceRpm));
        this.RaisePropertyChanged(nameof(PowerCadenceMaximumCadenceRpm));
        this.RaisePropertyChanged(nameof(CadenceTargetText));
        this.RaisePropertyChanged(nameof(NextSegmentText));
        this.RaisePropertyChanged(nameof(RampEstimateText));
        NotifyClockChanged();
    }

    private static string FormatRemainingTime(TimeSpan remaining)
    {
        var seconds = (int)double.Ceiling(remaining.TotalSeconds);
        return $"{seconds / 60:D2}:{seconds % 60:D2}";
    }

    private void RecordAdherence(CompiledTrainingSegment? segment, DateTimeOffset completedAt)
    {
        if (segment is not { Definition.CountsTowardAdherence: true } || segmentStartedAt is not { } startedAt)
        {
            return;
        }

        var samples = dashboard.GetRecordedSamplesSince(startedAt)
            .Select(static sample => new TrainingAdherenceSample(sample.CapturedAt, sample.CadenceRpm))
            .ToArray();
        var result = TrainingAdherence.Calculate(
            samples,
            segment.Definition.MinimumCadenceRpm,
            segment.Definition.MaximumCadenceRpm,
            startedAt,
            completedAt,
            completed: true);
        AdherenceText = result switch
        {
            TrainingAdherenceScore score => $"Cadence in range: {score.Percentage:F0}% · {score.Stars} star{(score.Stars == 1 ? string.Empty : "s")}",
            TrainingAdherenceNoCadenceTarget => "Cadence score unavailable: this interval has no cadence target.",
            TrainingAdherenceNoEligibleCadence => "Cadence score unavailable: no cadence was measured after the trainer settling period.",
        };
    }
}