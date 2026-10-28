using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

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
    private CompiledTrainingWorkout? activeWorkout;
    private IReadOnlyList<TrainingSegment> activeSegments = [];
    private IReadOnlyList<TelemetrySample> targetPowerSamples = [];
    private bool isSessionStartPending;
    private TrainingPowerCapabilities rampCapabilities;
    private DateTimeOffset? rampStartedAt;
    private DateTimeOffset? segmentStartedAt;
    private RampFtpEstimate? rampEstimate;
    private bool isRampTest;
    private bool isPausePending;
    private bool isEnding;
    private bool isEndFailed;
    private bool isLocallyEndable;
    private bool hasSessionStarted;
    private long sessionGeneration;
    private Task<bool>? sessionStartTask;
    private TrainingBrowseMode browseMode = TrainingBrowseMode.Workouts;
    private string statusText = "Choose a workout.";
    private string adherenceText = "Precision score appears after a work interval.";
    private int currentTargetWatts;
    private bool isCatalogLoading = true;
    private bool hasPublishedPlans;
    private bool isStarting;

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
        ShowHomeCommand = ReactiveCommand.Create(() => dashboard.RequestNavigation(DashboardDestination.Today));
        var hasPowerTarget = dashboard.WhenAnyValue(
                viewModel => viewModel.IsConnected,
                viewModel => viewModel.PowerTargetControl,
                static (connected, power) => connected && power is not null);
        var hasFunctionalThresholdPower = dashboard.WhenAnyValue(viewModel => viewModel.Telemetry.FunctionalThresholdPowerText)
            .Select(static text => ushort.TryParse(text, out var watts) && watts > 0);
        var isIdle = this.WhenAnyValue(viewModel => viewModel.IsActive).Select(static active => !active);
        StartCommand = ReactiveCommand.CreateFromTask(
            StartAsync,
            Observable.CombineLatest(
                    this.WhenAnyValue(viewModel => viewModel.SelectedWorkout).Select(static workout => workout is not null),
                    hasPowerTarget,
                    hasFunctionalThresholdPower,
                    isIdle,
                    static (selected, power, ftp, idle) => selected && power && ftp && idle)
                .ObserveOn(RxApp.MainThreadScheduler));
        disposables.Add(StartCommand.IsExecuting
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(value => IsStarting = value));
        disposables.Add(StartCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(exception => StatusText = exception is OperationCanceledException
                ? "Starting training was cancelled."
                : $"Training could not start: {exception.Message}"));
        StartRampTestCommand = ReactiveCommand.CreateFromTask(
            StartRampTestAsync,
            Observable.CombineLatest(hasPowerTarget, isIdle, static (power, idle) => power && idle).ObserveOn(RxApp.MainThreadScheduler));
        disposables.Add(StartRampTestCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(exception => StatusText = $"The ramp test could not start: {exception.Message}"));
        PauseCommand = ReactiveCommand.CreateFromTask(PauseAsync, this.WhenAnyValue(viewModel => viewModel.CanPause));
        ResumeCommand = ReactiveCommand.CreateFromTask(ResumeAsync, this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state == TrainingWorkoutState.Paused));
        RetryCommand = ReactiveCommand.CreateFromTask(RetryAsync, this.WhenAnyValue(viewModel => viewModel.RunnerState).Select(state => state == TrainingWorkoutState.TargetFailed));
        EndCommand = ReactiveCommand.CreateFromTask(EndAsync, this.WhenAnyValue(viewModel => viewModel.CanEnd));
        FinishRampTestCommand = ReactiveCommand.CreateFromTask(FinishRampTestAsync, this.WhenAnyValue(viewModel => viewModel.CanFinishRampTest));
        AcceptRampEstimateCommand = ReactiveCommand.CreateFromTask(AcceptRampEstimateAsync, this.WhenAnyValue(viewModel => viewModel.RampEstimate).Select(static estimate => estimate is RampFtpEstimateReady));
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
            if (isCatalogLoading != value)
            {
                this.RaiseAndSetIfChanged(ref isCatalogLoading, value);
                this.RaisePropertyChanged(nameof(IsCatalogLoaded));
            }
        }
    }

    public bool IsCatalogLoaded => !IsCatalogLoading && Workouts.Count > 0;

    public bool IsStarting
    {
        get => isStarting;
        private set => this.RaiseAndSetIfChanged(ref isStarting, value);
    }

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
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Target {CurrentTargetWatts} W")
        : string.Empty;

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

    public RampFtpEstimate? RampEstimate
    {
        get => rampEstimate;
        private set => this.RaiseAndSetIfChanged(ref rampEstimate, value);
    }

    public string RampEstimateText => RampEstimate switch
    {
        RampFtpEstimateReady estimate => $"Estimated FTP: {estimate.EstimatedFtpWatts} W. Review and accept to update zones.",
        RampFtpEstimateDeviceMaximumReached => "Ramp test was inconclusive because the bike reached its advertised power maximum.",
        RampFtpEstimateInsufficientTelemetry => "Ramp test was inconclusive because a continuous measured minute was not recorded.",
        null => "",
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

    public string CurrentWorkoutName => IsRampTest ? "FTP ramp test" : activeWorkout?.Definition.Name ?? SelectedWorkout?.Name ?? "Workout";

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

    public ReactiveCommand<Unit, Unit> ShowHomeCommand { get; }

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
            var compiledWorkout = TrainingWorkoutCompiler.Compile(workout, ftp, capabilities);
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

    private void SelectWorkout(TrainingWorkout workout)
    {
        SelectedWorkout = workout;
        StatusText = $"{workout.Name} selected.";
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
        this.RaisePropertyChanged(nameof(WorkoutsTabAccessibleName));
        this.RaisePropertyChanged(nameof(PlansTabAccessibleName));
        this.RaisePropertyChanged(nameof(AssessmentTabAccessibleName));
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

        isRampTest = true;
        rampStartedAt = DateTimeOffset.UtcNow;
        RampEstimate = null;
        await BeginAsync(RampFtpProtocol.Create(rampCapabilities), cancellationToken);
    }

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

    private async Task FinishRampTestAsync(CancellationToken cancellationToken)
    {
        await FinalizeRampTestAsync(cancellationToken, allStepsExhausted: false);
    }

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
        AdherenceText = "Precision score appears after a work interval.";
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
        if (segment is not { Definition.CountsTowardAdherence: true } || segmentStartedAt is not { } startedAt || !dashboard.TryGetTrainingPowerCapabilities(out var capabilities))
        {
            return;
        }

        var samples = dashboard.GetRecordedSamplesSince(startedAt)
            .Select(static sample => new TrainingAdherenceSample(sample.CapturedAt, sample.PowerWatts))
            .ToArray();
        var result = TrainingAdherence.Calculate(samples, segment.TargetWatts, capabilities.IncrementWatts, startedAt, completedAt, completed: true);
        AdherenceText = result switch
        {
            TrainingAdherenceScore score => $"Precision: {score.Percentage:F0}% · {score.Stars} star{(score.Stars == 1 ? string.Empty : "s")}",
            TrainingAdherenceNoMeasuredPower => "Precision unavailable: no measured power was recorded.",
        };
    }
}