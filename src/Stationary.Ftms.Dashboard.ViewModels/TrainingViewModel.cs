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
    private TrainingPowerCapabilities? targetRampCapabilities;
    private TrainingTargetRamp activeTargetRamp;
    private TimeSpan activeTargetRampStartElapsed;
    private TimeSpan segmentRampDuration;
    private int activeTargetRampSegmentIndex = -1;
    private int? lastAppliedTargetWatts;
    private long targetRevision;
    private bool isTargetRampStepPending;
    private bool requiresPedallingBeforeTarget;
    private bool isWaitingForPedalling;
    private readonly TrainingCadenceGuard cadenceGuard = new();
    private readonly TrainingResistanceFloor resistanceFloor = new();
    private int resistanceFloorNoticeSegmentIndex = -1;
    private int activePreferredCadenceRpm = TrainingWorkoutCompiler.ReferenceCadenceRpm;
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
        _cadenceDirectionTextHelper = Observable.CombineLatest(
                dashboard.WhenAnyValue(
                    viewModel => viewModel.Telemetry.CurrentCadenceRpm,
                    viewModel => viewModel.Telemetry.IsStale,
                    static (cadence, stale) => stale ? null : cadence),
                this.WhenAnyValue(
                    viewModel => viewModel.RunnerState,
                    viewModel => viewModel.PowerCadenceMinimumCadenceRpm,
                    viewModel => viewModel.PowerCadenceMaximumCadenceRpm),
                static (cadence, target) => GetCadenceDirectionText(cadence, target.Item1, target.Item2, target.Item3))
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.CadenceDirectionText, string.Empty);
        disposables.Add(_cadenceDirectionTextHelper);
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

    // Whole-workout axis bounds keep the map scale fixed so only the target zone moves between intervals.
    public int? PowerCadenceAxisMinimumCadenceRpm => IsActive
        ? activeWorkout?.Segments.Min(static segment => segment.Definition.MinimumCadenceRpm ?? segment.Definition.MaximumCadenceRpm)
        : null;

    public int? PowerCadenceAxisMaximumCadenceRpm => IsActive
        ? activeWorkout?.Segments.Max(static segment => segment.Definition.MaximumCadenceRpm ?? segment.Definition.MinimumCadenceRpm)
        : null;

    public double? PowerCadenceAxisMaximumWatts => IsActive && activeWorkout is { Segments.Count: > 0 } workout
        ? workout.Segments.Max(static segment => segment.TargetWatts)
        : null;

    [Reactive]
    public partial string CadenceCalibrationText { get; private set; } = string.Empty;

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

    private static readonly string WaitingForPedallingText = string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"Start pedalling. The workout begins at {TrainingCadenceGuard.PedallingCadenceRpm:F0} rpm.");

    private static readonly string StalledText = string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"Cadence too low, resistance eased. Spin up to {TrainingCadenceGuard.PedallingCadenceRpm:F0} rpm to resume.");

    private const int ClosestCadenceHintRpm = 3;

    public string IntervalGuidanceText
    {
        get
        {
            if (runner?.CurrentSegment is not { } segment)
            {
                return string.Empty;
            }

            if (isWaitingForPedalling)
            {
                return WaitingForPedallingText;
            }

            if (runner.State == TrainingWorkoutState.ApplyingTarget)
            {
                return "Applying ERG target...";
            }

            if (runner.State == TrainingWorkoutState.Paused)
            {
                return "Training paused.";
            }

            if (runner.State == TrainingWorkoutState.Running && cadenceGuard.IsStalled)
            {
                return StalledText;
            }

            if (runner.State == TrainingWorkoutState.Running
                && runner.GetCurrentSegmentRemaining(DateTimeOffset.UtcNow) is { } remaining)
            {
                var elapsed = segment.Definition.Duration - remaining;
                var rampEnd = GetActiveTargetRampEndElapsed();
                if (elapsed < rampEnd)
                {
                    return $"Ramping toward {segment.TargetWatts} W. Keep cadence steady.";
                }

                if (elapsed < rampEnd + GetSettlingDuration(runner.SegmentIndex)
                    && (IsCadenceTransition(runner.SegmentIndex) || ChangesPowerTarget(runner.SegmentIndex)))
                {
                    return IsCadenceTransition(runner.SegmentIndex)
                        ? "Move cadence smoothly into the new range; the trainer holds power by changing resistance."
                        : "Keep cadence steady while the trainer adjusts resistance.";
                }

                if (GetLiveCadenceRpm() is double liveCadence
                    && resistanceFloor.IsAboveTarget(segment.TargetWatts, liveCadence)
                    && resistanceFloor.EstimateMinimumWatts(liveCadence) is int minimumWatts)
                {
                    var floorText = $"The bike's lightest resistance is about {minimumWatts} W at {liveCadence:F0} rpm, so power reads above the {segment.TargetWatts} W target.";
                    return resistanceFloor.GetClosestCadence(segment.TargetWatts, segment.Definition.MinimumCadenceRpm) is int closestCadence
                        && closestCadence <= liveCadence - ClosestCadenceHintRpm
                        ? $"{floorText} Ride near {closestCadence} rpm to get closer."
                        : $"{segment.Definition.Cue} {floorText}";
                }
            }

            return segment.Definition.Cue;
        }
    }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string CadenceDirectionText { get; }

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
        IReadOnlyList<TrainingWorkout> catalogWorkouts = [.. catalog.Workouts, .. TrainingDebugCatalog.Workouts];
        Plans = [.. catalog.Plans, TrainingDebugCatalog.Plan];
        workoutsById = catalogWorkouts.ToDictionary(static workout => workout.Id, StringComparer.Ordinal);
        SelectedWorkout = catalogWorkouts.FirstOrDefault();
        SelectedPlan = Plans.FirstOrDefault();
        SelectedPlanWeek = SelectedPlan?.Weeks.FirstOrDefault();

        workouts.Clear();
        for (var workoutIndex = 0; workoutIndex < catalogWorkouts.Count; workoutIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            workouts.Add(catalogWorkouts[workoutIndex]);

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
            var preferredCadence = dashboard.Telemetry.PreferredCadenceRpm;
            var compiledWorkout = TrainingWorkoutCompiler.Compile(
                workout,
                ftp,
                capabilities,
                preferredCadence);
            isRampTest = false;
            targetRampCapabilities = capabilities;
            activePreferredCadenceRpm = preferredCadence;
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
        targetRampCapabilities = null;
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

        await StartSegmentTargetAsync(cancellationToken, generation);
    }

    [ReactiveCommand(CanExecute = nameof(canEnd))]
    private async Task EndAsync(CancellationToken cancellationToken)
    {
        if (isEnding)
        {
            return;
        }

        dashboard.AddTrainingEvent(runner?.CurrentSegment is { } endedSegment
            ? $"Workout ended by rider during {endedSegment.Definition.Name}"
            : "Workout ended by rider");
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
        isWaitingForPedalling = false;
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

        var cadence = GetLiveCadenceRpm();
        if (isWaitingForPedalling)
        {
            if (TrainingCadenceGuard.IsPedalling(cadence))
            {
                dashboard.AddTrainingEvent(TrainingEventLabels.Pedalling(cadence));
                isWaitingForPedalling = false;
                requiresPedallingBeforeTarget = false;
                _ = ApplyCurrentTargetAsync(CancellationToken.None, sessionGeneration);
            }

            return;
        }

        var completedSegment = runner?.CurrentSegment;
        var completedSegmentIndex = runner?.SegmentIndex ?? -1;
        if (runner?.Advance(now) != true)
        {
            GuardCadence(now, cadence);
            ObserveResistanceFloor(now, cadence);
            ContinueTargetRamp(now);
            return;
        }

        RecordAdherence(completedSegment, completedSegmentIndex, now);
        CalibrateCadence(completedSegmentIndex);
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

    private double? GetLiveCadenceRpm() => dashboard.Telemetry.IsStale ? null : dashboard.Telemetry.CurrentCadenceRpm;

    private double? GetLivePowerWatts() => dashboard.Telemetry.IsStale ? null : dashboard.Telemetry.CurrentPowerWatts;

    private bool IsTargetSettled(DateTimeOffset now) =>
        runner is { State: TrainingWorkoutState.Running } activeRunner
        && !cadenceGuard.IsStalled
        && activeRunner.GetCurrentSegmentRemaining(now) is { } remaining
        && activeRunner.CurrentSegment is { } segment
        && segment.Definition.Duration - remaining >= GetActiveTargetRampEndElapsed() + GetSettlingDuration(activeRunner.SegmentIndex);

    private void ObserveResistanceFloor(DateTimeOffset now, double? cadence)
    {
        if (isRampTest || runner is not { CurrentSegment: { } segment } activeRunner)
        {
            return;
        }

        var settled = IsTargetSettled(now);
        if (resistanceFloor.Observe(now, GetLivePowerWatts(), cadence, segment.TargetWatts, settled)
            && resistanceFloor.WattsPerRpm is double wattsPerRpm
            && cadence is double floorCadence)
        {
            dashboard.AddTrainingEvent(TrainingEventLabels.ResistanceFloor(wattsPerRpm, floorCadence));
        }

        if (settled
            && cadence is double liveCadence
            && resistanceFloorNoticeSegmentIndex != activeRunner.SegmentIndex
            && resistanceFloor.IsAboveTarget(segment.TargetWatts, liveCadence)
            && resistanceFloor.EstimateMinimumWatts(liveCadence) is int minimumWatts)
        {
            resistanceFloorNoticeSegmentIndex = activeRunner.SegmentIndex;
            dashboard.AddTrainingEvent(TrainingEventLabels.TargetBelowFloor(segment.TargetWatts, minimumWatts, liveCadence));
        }
    }

    // The warm-up shows the rider's natural cadence on this bike; later cadence ranges are rescaled to it within a bounded adjustment.
    private void CalibrateCadence(int completedSegmentIndex)
    {
        if (isRampTest
            || runner is null
            || activeWorkout is not { } workout
            || segmentStartedAt is not { } startedAt
            || !TrainingCadenceCalibration.IsCalibrationSegment(workout, completedSegmentIndex))
        {
            return;
        }

        var samples = dashboard.GetRecordedSamplesSince(startedAt + segmentRampDuration + GetSettlingDuration(completedSegmentIndex))
            .Where(static sample => sample.CadenceRpm is not null)
            .Select(static sample => sample.CadenceRpm!.Value);
        if (TrainingCadenceCalibration.MeasureMedianRpm(samples) is not { } measured)
        {
            CadenceCalibrationText = $"Not enough warm-up cadence to tune targets; using {activePreferredCadenceRpm} rpm.";
            dashboard.AddTrainingEvent(CadenceCalibrationText);
            return;
        }

        var calibrated = TrainingCadenceCalibration.Limit(measured, activePreferredCadenceRpm);
        if (calibrated == activePreferredCadenceRpm)
        {
            CadenceCalibrationText = measured == calibrated
                ? $"Warm-up cadence matches {calibrated} rpm; targets unchanged."
                : $"Warm-up cadence {measured} rpm; targets stay at {calibrated} rpm.";
            dashboard.AddTrainingEvent(CadenceCalibrationText);
            return;
        }

        var rescaled = TrainingWorkoutCompiler.RescaleCadence(workout, calibrated, completedSegmentIndex + 1);
        activeWorkout = rescaled;
        runner.ReplaceWorkout(rescaled);
        activeSegments = [.. rescaled.Segments.Select(static segment => segment.Definition)];
        this.RaisePropertyChanged(nameof(ActiveSegments));
        CadenceCalibrationText = measured == calibrated
            ? $"Cadence targets tuned to your {calibrated} rpm warm-up."
            : $"Warm-up cadence {measured} rpm; targets tuned to {calibrated} rpm, the {TrainingCadenceCalibration.MaximumAdjustmentRpm} rpm limit per ride. Set Preferred cadence in Settings to match.";
        dashboard.AddTrainingEvent(CadenceCalibrationText);
    }

    private Task StartSegmentTargetAsync(CancellationToken cancellationToken, long generation) =>
        requiresPedallingBeforeTarget
            ? HoldUntilPedallingAsync(cancellationToken, generation)
            : ApplyCurrentTargetAsync(cancellationToken, generation);

    // Holding the lowest target until the rider pedals prevents the trainer from loading resistance against a stopped flywheel.
    private async Task HoldUntilPedallingAsync(CancellationToken cancellationToken, long generation)
    {
        if (generation != sessionGeneration || runner is null || GetHoldCapabilities() is not { } capabilities)
        {
            return;
        }

        StatusText = "Preparing the bike...";
        if (!await dashboard.ApplyTrainingTargetAsync(capabilities.MinimumWatts, cancellationToken))
        {
            if (generation == sessionGeneration)
            {
                runner.RejectTarget();
                StatusText = "The bike did not accept the starting target. Retry or end training.";
                NotifyRunnerChanged();
            }

            return;
        }

        if (generation != sessionGeneration)
        {
            return;
        }

        CurrentTargetWatts = capabilities.MinimumWatts;
        dashboard.AddTrainingEvent($"Waiting for pedalling · holding {capabilities.MinimumWatts} W");
        isWaitingForPedalling = true;
        StatusText = WaitingForPedallingText;
        NotifyRunnerChanged();
    }

    private TrainingPowerCapabilities? GetHoldCapabilities() => isRampTest ? rampCapabilities : targetRampCapabilities;

    private async Task ApplyCurrentTargetAsync(CancellationToken cancellationToken, long generation)
    {
        if (generation != sessionGeneration || runner?.CurrentSegment is not { } segment)
        {
            return;
        }

        var revision = ++targetRevision;
        var segmentIndex = runner.SegmentIndex;
        if (activeWorkout is { } workout)
        {
            dashboard.AddTrainingEvent(TrainingEventLabels.Interval(segmentIndex, workout.Segments.Count, segment));
        }

        var isHeldLow = cadenceGuard.IsStalled && targetRampCapabilities is not null;
        var ramp = targetRampCapabilities is { } capabilities && !isHeldLow
            ? TrainingTargetRamp.Create(lastAppliedTargetWatts, segment, capabilities)
            : new TrainingTargetRamp(segment.TargetWatts, segment.TargetWatts, TimeSpan.Zero);
        var initialWatts = targetRampCapabilities is { } rampCapabilities
            ? isHeldLow ? rampCapabilities.MinimumWatts : ramp.GetTargetWatts(TimeSpan.Zero, rampCapabilities)
            : segment.TargetWatts;
        CurrentTargetWatts = initialWatts;
        StatusText = ramp.Duration > TimeSpan.Zero
            ? $"Ramping toward {segment.TargetWatts} W for {segment.Definition.Name}."
            : $"Applying {segment.TargetWatts} W for {segment.Definition.Name}.";
        if (await dashboard.ApplyTrainingTargetAsync(initialWatts, cancellationToken))
        {
            if (generation != sessionGeneration)
            {
                return;
            }

            segmentStartedAt = DateTimeOffset.UtcNow;
            if (revision == targetRevision)
            {
                activeTargetRamp = ramp;
                activeTargetRampStartElapsed = TimeSpan.Zero;
                activeTargetRampSegmentIndex = segmentIndex;
                lastAppliedTargetWatts = initialWatts;
            }

            segmentRampDuration = ramp.Duration;
            runner.ConfirmTargetApplied(segmentStartedAt.Value);
            if (dashboard.SessionState == DashboardViewModel.WorkoutSessionState.Paused)
            {
                runner.Pause(segmentStartedAt.Value);
            }

            TargetPowerSamples = [.. TargetPowerSamples, new TelemetrySample(segmentStartedAt.Value, initialWatts)];
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

    private void GuardCadence(DateTimeOffset now, double? cadence)
    {
        if (isRampTest
            || targetRampCapabilities is not { } capabilities
            || runner is not { State: TrainingWorkoutState.Running, CurrentSegment: { } segment } activeRunner
            || activeRunner.GetCurrentSegmentRemaining(now) is not { } remaining)
        {
            return;
        }

        var transition = cadenceGuard.Update(now, cadence);
        switch (transition)
        {
            case TrainingCadenceGuardTransition.Stalled:
                dashboard.AddTrainingEvent(TrainingEventLabels.Stalled(cadence, capabilities.MinimumWatts));
                targetRevision++;
                activeTargetRamp = default;
                StatusText = StalledText;
                _ = ApplyHeldTargetAsync(capabilities.MinimumWatts, sessionGeneration, targetRevision);
                NotifyClockChanged();
                break;
            case TrainingCadenceGuardTransition.Recovered:
                dashboard.AddTrainingEvent(TrainingEventLabels.Recovered(cadence, segment.TargetWatts));
                targetRevision++;
                // Start from what the rider already produces, and never below the bike's floor at this cadence:
                // targets under the floor left the restart spike in place, while targets at or above it ended it.
                var rampFrom = int.Max(
                    int.Max(lastAppliedTargetWatts ?? capabilities.MinimumWatts, GetLivePowerWatts() is double livePower ? (int)livePower : 0),
                    cadence is double recoveredCadence ? resistanceFloor.EstimateMinimumWatts(recoveredCadence) ?? 0 : 0);
                activeTargetRamp = TrainingTargetRamp.Create(rampFrom, segment, capabilities);
                activeTargetRampStartElapsed = segment.Definition.Duration - remaining;
                activeTargetRampSegmentIndex = activeRunner.SegmentIndex;
                StatusText = $"Cadence recovered. Ramping back to {segment.TargetWatts} W.";
                if (activeTargetRamp.Duration <= TimeSpan.Zero)
                {
                    _ = ApplyHeldTargetAsync(segment.TargetWatts, sessionGeneration, targetRevision);
                }

                NotifyClockChanged();
                break;
        }
    }

    private async Task ApplyHeldTargetAsync(int watts, long generation, long revision)
    {
        if (!await dashboard.ApplyTrainingTargetAsync(watts, CancellationToken.None)
            || generation != sessionGeneration
            || revision != targetRevision)
        {
            return;
        }

        lastAppliedTargetWatts = watts;
        CurrentTargetWatts = watts;
        TargetPowerSamples = [.. TargetPowerSamples, new TelemetrySample(DateTimeOffset.UtcNow, watts)];
        NotifyRunnerChanged();
    }

    private void ContinueTargetRamp(DateTimeOffset now)
    {
        if (isTargetRampStepPending
            || cadenceGuard.IsStalled
            || targetRampCapabilities is not { } capabilities
            || runner is not { State: TrainingWorkoutState.Running, CurrentSegment: { } segment } activeRunner
            || activeRunner.SegmentIndex != activeTargetRampSegmentIndex
            || activeTargetRamp.Duration <= TimeSpan.Zero
            || activeRunner.GetCurrentSegmentRemaining(now) is not { } remaining)
        {
            return;
        }

        var rampElapsed = segment.Definition.Duration - remaining - activeTargetRampStartElapsed;
        var watts = activeTargetRamp.GetTargetWatts(rampElapsed, capabilities);
        if (watts != lastAppliedTargetWatts)
        {
            _ = ApplyTargetRampStepAsync(watts, sessionGeneration, targetRevision);
        }
    }

    private async Task ApplyTargetRampStepAsync(int watts, long generation, long revision)
    {
        isTargetRampStepPending = true;
        try
        {
            if (!await dashboard.ApplyTrainingTargetAsync(watts, CancellationToken.None)
                || generation != sessionGeneration
                || revision != targetRevision)
            {
                return;
            }

            lastAppliedTargetWatts = watts;
            CurrentTargetWatts = watts;
            TargetPowerSamples = [.. TargetPowerSamples, new TelemetrySample(DateTimeOffset.UtcNow, watts)];
            NotifyRunnerChanged();
        }
        finally
        {
            isTargetRampStepPending = false;
        }
    }

    private TimeSpan GetActiveTargetRampEndElapsed() =>
        runner?.SegmentIndex == activeTargetRampSegmentIndex
            ? activeTargetRampStartElapsed + activeTargetRamp.Duration
            : TimeSpan.Zero;

    private static string GetCadenceDirectionText(double? cadence, TrainingWorkoutState state, int? minimum, int? maximum) =>
        state == TrainingWorkoutState.Running
            ? TrainingCadenceGuidance.GetDirectionText(cadence, minimum, maximum)
            : string.Empty;

    private async Task CompleteWorkoutAsync()
    {
        if (isRampTest)
        {
            await FinalizeRampTestAsync(CancellationToken.None, allStepsExhausted: true);
            return;
        }

        dashboard.AddTrainingEvent("Workout complete");
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
        lastAppliedTargetWatts = null;
        activeTargetRamp = default;
        activeTargetRampStartElapsed = TimeSpan.Zero;
        segmentRampDuration = TimeSpan.Zero;
        activeTargetRampSegmentIndex = -1;
        targetRevision++;
        cadenceGuard.Reset();
        resistanceFloor.Reset();
        resistanceFloorNoticeSegmentIndex = -1;
        CadenceCalibrationText = string.Empty;
        requiresPedallingBeforeTarget = true;
        isWaitingForPedalling = false;
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
                dashboard.AddTrainingEvent(TrainingEventLabels.Workout(workout.Definition.Name, dashboard.Telemetry.FunctionalThresholdPowerText, activePreferredCadenceRpm));
                await StartSegmentTargetAsync(cancellationToken, generation);
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
        isWaitingForPedalling = false;
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
        this.RaisePropertyChanged(nameof(PowerCadenceAxisMinimumCadenceRpm));
        this.RaisePropertyChanged(nameof(PowerCadenceAxisMaximumCadenceRpm));
        this.RaisePropertyChanged(nameof(PowerCadenceAxisMaximumWatts));
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

    private TrainingSegment? GetSegmentDefinition(int segmentIndex) =>
        activeWorkout is { } workout && segmentIndex >= 0 && segmentIndex < workout.Segments.Count
            ? workout.Segments[segmentIndex].Definition
            : null;

    private bool IsCadenceTransition(int segmentIndex) =>
        GetSegmentDefinition(segmentIndex) is { } segment
        && TrainingAdherence.ChangesCadenceTarget(GetSegmentDefinition(segmentIndex - 1), segment);

    private bool ChangesPowerTarget(int segmentIndex) =>
        activeWorkout is not { } workout
        || segmentIndex <= 0
        || segmentIndex >= workout.Segments.Count
        || workout.Segments[segmentIndex].TargetWatts != workout.Segments[segmentIndex - 1].TargetWatts;

    private TimeSpan GetSettlingDuration(int segmentIndex) => IsCadenceTransition(segmentIndex)
        ? TrainingAdherence.CadenceTransitionDuration
        : TrainingAdherence.TrainerSettlingDuration;

    private void RecordAdherence(CompiledTrainingSegment? segment, int segmentIndex, DateTimeOffset completedAt)
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
            startedAt + segmentRampDuration,
            completedAt,
            completed: true,
            GetSettlingDuration(segmentIndex));
        AdherenceText = result switch
        {
            TrainingAdherenceScore score => $"Cadence in range: {score.Percentage:F0}% · {score.Stars} star{(score.Stars == 1 ? string.Empty : "s")}",
            TrainingAdherenceNoCadenceTarget => "Cadence score unavailable: this interval has no cadence target.",
            TrainingAdherenceNoEligibleCadence => "Cadence score unavailable: no cadence was measured after the trainer settling period.",
        };
    }
}