using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.ViewModels;

// Workout execution is a feedback loop: events fold through the pure TrainingSessionEngine into
// immutable TrainingSession snapshots, and the emitted effects run against the bike and feed their results back.
public sealed partial class TrainingViewModel : ReactiveObject, IDisposable
{
    private const int CardBatchSize = 5;
    private const int ClosestCadenceHintRpm = 3;
    private readonly DashboardViewModel dashboard;
    private readonly CompositeDisposable disposables = [];
    private readonly Subject<TrainingSessionEvent> events = new();
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
    private TrainingPlanWeek? selectedPlanWeek;
    private bool isCatalogLoading = true;
    private IReadOnlyList<TrainingWorkout> catalogWorkouts = [];
    private readonly List<TrainingWorkout> customWorkouts = [];

    public TrainingViewModel(DashboardViewModel dashboard)
    {
        this.dashboard = dashboard;
        Workouts = new ReadOnlyObservableCollection<TrainingWorkout>(workouts);

        var engine = new TrainingSessionEngine(dashboard.GetRecordedSamplesSince);
        // ObserveOn serializes events dispatched while a previous event is still being reduced.
        var transitions = events
            .ObserveOn(RxApp.MainThreadScheduler)
            .Scan(TrainingTransition.Of(TrainingSession.Idle), (transition, sessionEvent) => engine.Reduce(transition.State, sessionEvent))
            .Publish();
        _sessionHelper = transitions
            .Select(static transition => transition.State)
            .DistinctUntilChanged<TrainingSession>(ReferenceEqualityComparer.Instance)
            .ToProperty(this, viewModel => viewModel.Session, TrainingSession.Idle, scheduler: ImmediateScheduler.Instance);
        disposables.Add(_sessionHelper);
        disposables.Add(this.WhenAnyValue(viewModel => viewModel.Session)
            .Skip(1)
            .Subscribe(_ => NotifySessionChanged()));
        disposables.Add(transitions
            .SelectMany(static transition => transition.Effects)
            .Select(Execute)
            .Merge()
            .Subscribe(events.OnNext, exception => Post($"Training updates failed: {exception.Message}")));
        disposables.Add(transitions.Connect());

        _activeSegmentsHelper = this.WhenAnyValue(viewModel => viewModel.Session)
            .Select(static session => session.Workout)
            .DistinctUntilChanged()
            .Select(static workout => workout is null
                ? (IReadOnlyList<TrainingSegment>)[]
                : [.. workout.Segments.Select(static segment => segment.Definition)])
            .ToProperty(this, viewModel => viewModel.ActiveSegments, [], scheduler: ImmediateScheduler.Instance);
        disposables.Add(_activeSegmentsHelper);

        canSelect = this.WhenAnyValue(viewModel => viewModel.IsActive).Select(static active => !active);
        var hasPowerTarget = dashboard.WhenAnyValue(
                viewModel => viewModel.IsConnected,
                viewModel => viewModel.PowerTargetControl,
                static (connected, power) => connected && power is not null);
        var hasFunctionalThresholdPower = dashboard.WhenAnyValue(viewModel => viewModel.Telemetry.FunctionalThresholdPowerText)
            .Select(static text => ushort.TryParse(text, out var watts) && watts > 0);
        var workoutRequirements = this.WhenAnyValue(viewModel => viewModel.SelectedWorkout)
            .Select(static workout => (
                HasWorkout: workout is not null,
                NeedsHeartRate: workout?.Segments.Any(static segment => segment.HeartRateTarget is not null) == true,
                NeedsZones: workout?.Kind is TrainingWorkoutKind.HeartRateZone or TrainingWorkoutKind.GeneralHealth
                    || workout?.Segments.Any(static segment => segment.HeartRateTarget?.ZoneCode is not null) == true,
                RequiresHealthScreening: workout?.Kind == TrainingWorkoutKind.GeneralHealth,
                RequiresClinicianConfirmation: workout?.Kind == TrainingWorkoutKind.ClinicianPrescribed));
        var heartRateReadiness = dashboard.HeartRate.WhenAnyValue(
            viewModel => viewModel.LatestObservation,
            viewModel => viewModel.IsStale,
            viewModel => viewModel.IsZoneProfileConfigured,
            static (observation, stale, profile) => (HasFreshObservation: observation is not null && !stale, HasProfile: profile));
        var startAcknowledgements = this.WhenAnyValue(
            viewModel => viewModel.HasCompletedHealthScreening,
            viewModel => viewModel.HasConfirmedClinicianPrescription,
            static (health, clinician) => (Health: health, Clinician: clinician));
        var selectedWorkoutReady = Observable.CombineLatest(
            workoutRequirements,
            dashboard.WhenAnyValue(viewModel => viewModel.IsHeartRateConnected),
            heartRateReadiness,
            startAcknowledgements,
            static (requirements, connected, heartRate, acknowledgements) =>
                requirements.HasWorkout
                && (!requirements.NeedsHeartRate || connected && heartRate.HasFreshObservation)
                && (!requirements.NeedsZones || heartRate.HasProfile)
                && (!requirements.RequiresHealthScreening || acknowledgements.Health)
                && (!requirements.RequiresClinicianConfirmation || acknowledgements.Clinician));
        canStart = Observable.CombineLatest(
                    selectedWorkoutReady,
                    hasPowerTarget,
                    hasFunctionalThresholdPower,
                    canSelect,
                    static (selected, power, ftp, idle) => selected && power && ftp && idle)
                .ObserveOn(RxApp.MainThreadScheduler);
        canStartRampTest = Observable.CombineLatest(hasPowerTarget, canSelect, static (power, idle) => power && idle).ObserveOn(RxApp.MainThreadScheduler);
        var session = this.WhenAnyValue(viewModel => viewModel.Session);
        canPause = session.Select(static value => value.CanPause).DistinctUntilChanged();
        canResume = session.Select(static value => value.CanResume).DistinctUntilChanged();
        canRetry = session.Select(static value => value.CanRetry).DistinctUntilChanged();
        canEnd = session.Select(static value => value.IsActive).DistinctUntilChanged();
        canFinishRampTest = session.Select(static value => value.CanFinishRampTest).DistinctUntilChanged();
        canAcceptRampEstimate = session.Select(static value => value.RampEstimate is RampFtpEstimateReady).DistinctUntilChanged();

        disposables.Add(Observable.Merge(
                StartCommand.ThrownExceptions.Select(static exception => $"Training could not start: {exception.Message}"),
                StartRampTestCommand.ThrownExceptions.Select(static exception => $"The ramp test could not start: {exception.Message}"),
                Observable.Merge(
                        PauseCommand.ThrownExceptions,
                        ResumeCommand.ThrownExceptions,
                        RetryCommand.ThrownExceptions,
                        EndCommand.ThrownExceptions,
                        FinishRampTestCommand.ThrownExceptions,
                        AcceptRampEstimateCommand.ThrownExceptions)
                    .Select(static exception => $"Training operation failed: {exception.Message}"),
                LoadCatalogCommand.ThrownExceptions.Select(_ =>
                {
                    IsCatalogLoading = false;
                    return "The training library could not be loaded.";
                }))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(Post));
        _cadenceDirectionTextHelper = Observable.CombineLatest(
                dashboard.WhenAnyValue(
                    viewModel => viewModel.Telemetry.CurrentCadenceRpm,
                    viewModel => viewModel.Telemetry.IsStale,
                    static (cadence, stale) => stale ? null : cadence),
                this.WhenAnyValue(
                    viewModel => viewModel.RunnerState,
                    viewModel => viewModel.PowerCadenceMinimumCadenceRpm,
                    viewModel => viewModel.PowerCadenceMaximumCadenceRpm),
                static (cadence, target) => target.Item1 == TrainingWorkoutState.Running
                    ? TrainingCadenceGuidance.GetDirectionText(cadence, target.Item2, target.Item3)
                    : string.Empty)
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.CadenceDirectionText, string.Empty);
        disposables.Add(_cadenceDirectionTextHelper);
        // ThrownExceptions above reports failures; Execute also needs its own error observer.
        disposables.Add(LoadCatalogCommand.Execute().Subscribe(static _ => { }, static _ => { }));
        disposables.Add(Observable.Interval(TimeSpan.FromSeconds(1), RxApp.TaskpoolScheduler)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                Dispatch(new ClockTicked(DateTimeOffset.UtcNow, ReadTelemetry()));
                NotifyClockChanged();
            }));
        disposables.Add(Observable.CombineLatest(
                dashboard.WhenAnyValue(viewModel => viewModel.IsConnected),
                dashboard.WhenAnyValue(viewModel => viewModel.SessionState),
                static (connected, state) => new TrainingMachine(connected, ToMachineState(state)))
            .DistinctUntilChanged()
            .Subscribe(machine => Dispatch(new MachineStateChanged(DateTimeOffset.UtcNow, machine))));
        disposables.Add(Observable.CombineLatest(
                dashboard.HeartRate.WhenAnyValue(viewModel => viewModel.ZoneProfile),
                dashboard.WhenAnyValue(viewModel => viewModel.IsHeartRateConnected),
                static (profile, connected) => profile is not null && connected)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => RefreshHeartRateWorkouts()));
    }

    [ObservableAsProperty(InitialValue = "global::Stationary.Ftms.Dashboard.Core.TrainingSession.Idle")]
    public partial TrainingSession Session { get; }

    public ReadOnlyObservableCollection<TrainingWorkout> Workouts { get; }

    public DashboardViewModel Dashboard => dashboard;

    [Reactive]
    public partial IReadOnlyList<TrainingPlan> Plans { get; private set; } = [];

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

    public bool IsStarting => Session.IsStarting;

    [Reactive]
    public partial bool HasCompletedHealthScreening { get; private set; }

    [Reactive]
    public partial bool HasConfirmedClinicianPrescription { get; private set; }

    [Reactive]
    public partial string HealthWorkoutDurationText { get; set; } = "30";

    [Reactive]
    public partial string HeartRateReserveRestingText { get; set; } = string.Empty;

    [Reactive]
    public partial string ClinicianMinimumHeartRateText { get; set; } = string.Empty;

    [Reactive]
    public partial string ClinicianMaximumHeartRateText { get; set; } = string.Empty;

    [Reactive]
    public partial string ClinicianMaximumPowerText { get; set; } = string.Empty;

    [Reactive]
    public partial string ClinicianDurationText { get; set; } = "20";

    [Reactive]
    public partial string WeeklyLowIntensityText { get; private set; } = "Measured easy heart-rate minutes: --";

    public string HealthScreeningAcknowledgementText => HasCompletedHealthScreening
        ? "Health screening acknowledged"
        : "I have no concerning symptoms or known heart condition";

    public string ClinicianPrescriptionAcknowledgementText => HasConfirmedClinicianPrescription
        ? "Clinician prescription confirmed"
        : "I have an individual prescription from my care team";

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
            UpdateWeeklyLowIntensityText();
        }
    }

    public IReadOnlyList<TrainingWorkout> SelectedPlanWorkouts => SelectedPlanWeek is null
        ? []
        : [.. SelectedPlanWeek.WorkoutIds.Select(workoutId => workoutsById[workoutId])];

    private TrainingWorkoutRunner? Runner => Session.Runner;

    private CompiledTrainingWorkout? ActiveWorkout => Session.Workout;

    public TrainingWorkoutState RunnerState => Session.RunnerState;

    public bool IsActive => Session.IsActive;

    [ObservableAsProperty(InitialValue = "[]")]
    public partial IReadOnlyList<TrainingSegment> ActiveSegments { get; }

    public IReadOnlyList<TelemetrySample> TargetPowerSamples => IsActive ? Session.TargetPowerSamples : [];

    public double WorkoutProgress => GetWorkoutTotal() is { Ticks: > 0 } total
        ? double.Clamp(GetWorkoutElapsed(DateTimeOffset.UtcNow) / total, 0d, 1d)
        : 0d;

    public string WorkoutElapsedText => FormatRemainingTime(GetWorkoutElapsed(DateTimeOffset.UtcNow));

    public string WorkoutRemainingText => Runner is null
        ? "--:--"
        : FormatRemainingTime(GetWorkoutTotal() - GetWorkoutElapsed(DateTimeOffset.UtcNow));

    public string CurrentTargetText => IsActive && CurrentTargetWatts > 0
        ? Runner?.CurrentSegment is { TargetMinimumHeartRate: ushort minimum, TargetMaximumHeartRate: ushort maximum }
            ? string.Create(CultureInfo.InvariantCulture, $"HR {minimum}-{maximum} bpm · power {CurrentTargetWatts} W")
            : string.Create(CultureInfo.InvariantCulture, $"ERG target {CurrentTargetWatts} W")
        : string.Empty;

    public double? PowerCadenceTargetWatts => IsActive && CurrentTargetWatts > 0 ? CurrentTargetWatts : null;

    public int? PowerCadenceMinimumCadenceRpm => IsActive ? Runner?.CurrentSegment?.Definition.MinimumCadenceRpm : null;

    public int? PowerCadenceMaximumCadenceRpm => IsActive ? Runner?.CurrentSegment?.Definition.MaximumCadenceRpm : null;

    // Whole-workout axis bounds keep the map scale fixed so only the target zone moves between intervals.
    public int? PowerCadenceAxisMinimumCadenceRpm => IsActive
        ? ActiveWorkout?.Segments.Min(static segment => segment.Definition.MinimumCadenceRpm ?? segment.Definition.MaximumCadenceRpm)
        : null;

    public int? PowerCadenceAxisMaximumCadenceRpm => IsActive
        ? ActiveWorkout?.Segments.Max(static segment => segment.Definition.MaximumCadenceRpm ?? segment.Definition.MinimumCadenceRpm)
        : null;

    public double? PowerCadenceAxisMaximumWatts => IsActive && ActiveWorkout is { Segments.Count: > 0 } workout
        ? workout.Segments.Max(static segment => segment.TargetWatts)
        : null;

    public string CadenceCalibrationText => Session.CadenceCalibrationText;

    public string CadenceTargetText => Runner?.CurrentSegment?.Definition switch
    {
        { MinimumCadenceRpm: { } minimum, MaximumCadenceRpm: { } maximum } => $"Target {minimum}-{maximum} rpm",
        { MinimumCadenceRpm: { } minimum } => $"Target {minimum}+ rpm",
        { MaximumCadenceRpm: { } maximum } => $"Target up to {maximum} rpm",
        _ => string.Empty,
    };

    public string NextSegmentText => Runner is { } activeRunner && ActiveWorkout is { } workout
        ? activeRunner.SegmentIndex + 1 < workout.Segments.Count
            ? $"Next: {workout.Segments[activeRunner.SegmentIndex + 1].Definition.Name} · {FormatRemainingTime(workout.Segments[activeRunner.SegmentIndex + 1].Definition.Duration)} at {GetTargetDescription(workout.Segments[activeRunner.SegmentIndex + 1])}"
            : "Final interval"
        : string.Empty;

    public bool CanPause => Session.CanPause;

    public bool CanResume => Session.CanResume;

    public bool CanRetry => Session.CanRetry;

    public bool CanEnd => IsActive;

    public bool CanFinishRampTest => Session.CanFinishRampTest;

    public bool IsRampTest => Session.IsRampTest;

    public RampFtpEstimate? RampEstimate => Session.RampEstimate;

    public string RampEstimateText => TrainingSessionText.Describe(RampEstimate);

    public string StatusText => Session.StatusText;

    public string AdherenceText => Session.AdherenceText;

    public string CurrentSegmentText => Runner?.CurrentSegment is { } segment
        ? $"{segment.Definition.Name}: {GetTargetDescription(segment)}"
        : "No active interval";

    public string CurrentWorkoutName => IsRampTest ? "FTP ramp test" : ActiveWorkout?.Definition.Name ?? SelectedWorkout?.Name ?? "Workout";

    public string IntervalProgressText => Runner is { } activeRunner
        ? $"Interval {activeRunner.SegmentIndex + 1} of {activeRunner.SegmentCount}"
        : "No active interval";

    public string CurrentIntervalRemainingText => Runner?.GetCurrentSegmentRemaining(DateTimeOffset.UtcNow) is { } remaining
        ? FormatRemainingTime(remaining)
        : "--:--";

    public string IntervalGuidanceText => GetIntervalGuidance(Session, ReadTelemetry(), dashboard.HeartRate.LatestObservation, DateTimeOffset.UtcNow);

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string CadenceDirectionText { get; }

    public int CurrentTargetWatts => Session.CurrentTargetWatts;

    public void Dispose()
    {
        disposables.Dispose();
        events.Dispose();
        LoadCatalogCommand.Dispose();
        SelectWorkoutCommand.Dispose();
        SelectPlanCommand.Dispose();
        SelectPlanWeekCommand.Dispose();
        AcknowledgeHealthScreeningCommand.Dispose();
        AcknowledgeClinicianPrescriptionCommand.Dispose();
        CreateModerateHealthWorkoutCommand.Dispose();
        CreateVigorousHealthWorkoutCommand.Dispose();
        CreateClinicianPrescribedWorkoutCommand.Dispose();
        EditHealthWorkoutDurationCommand.Dispose();
        EditHeartRateReserveRestingCommand.Dispose();
        EditClinicianMinimumHeartRateCommand.Dispose();
        EditClinicianMaximumHeartRateCommand.Dispose();
        EditClinicianMaximumPowerCommand.Dispose();
        EditClinicianDurationCommand.Dispose();
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
        catalogWorkouts = [.. catalog.Workouts, .. TrainingDebugCatalog.Workouts];
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

        IsCatalogLoading = false;
        RefreshHeartRateWorkouts();
        UpdateWeeklyLowIntensityText();
    }

    [ReactiveCommand]
    private void AcknowledgeHealthScreening()
    {
        HasCompletedHealthScreening = true;
        this.RaisePropertyChanged(nameof(HealthScreeningAcknowledgementText));
    }

    [ReactiveCommand]
    private void AcknowledgeClinicianPrescription()
    {
        HasConfirmedClinicianPrescription = true;
        this.RaisePropertyChanged(nameof(ClinicianPrescriptionAcknowledgementText));
    }

    [ReactiveCommand]
    private void CreateModerateHealthWorkout() => CreateHealthWorkout(vigorous: false);

    [ReactiveCommand]
    private void CreateVigorousHealthWorkout() => CreateHealthWorkout(vigorous: true);

    [ReactiveCommand]
    private void EditHealthWorkoutDuration(string text) => HealthWorkoutDurationText = text;

    [ReactiveCommand]
    private void EditHeartRateReserveResting(string text) => HeartRateReserveRestingText = text;

    [ReactiveCommand]
    private void EditClinicianMinimumHeartRate(string text) => ClinicianMinimumHeartRateText = text;

    [ReactiveCommand]
    private void EditClinicianMaximumHeartRate(string text) => ClinicianMaximumHeartRateText = text;

    [ReactiveCommand]
    private void EditClinicianMaximumPower(string text) => ClinicianMaximumPowerText = text;

    [ReactiveCommand]
    private void EditClinicianDuration(string text) => ClinicianDurationText = text;

    [ReactiveCommand]
    private void CreateClinicianPrescribedWorkout()
    {
        if (!dashboard.IsHeartRateConnected)
        {
            Post("Connect a heart-rate sensor before creating a heart-rate session.");
            return;
        }

        if (!ushort.TryParse(ClinicianMinimumHeartRateText, out var minimum)
            || !ushort.TryParse(ClinicianMaximumHeartRateText, out var maximum)
            || !int.TryParse(ClinicianMaximumPowerText, out var maximumPower)
            || !int.TryParse(ClinicianDurationText, out var durationMinutes)
            || minimum is < 30 or > 250
            || maximum is < 30 or > 250
            || maximum < minimum
            || maximumPower <= 0
            || durationMinutes is < 1 or > 240)
        {
            Post("Enter a valid prescribed bpm range, positive power cap, and duration from 1 to 240 minutes.");
            return;
        }

        var workout = HeartRateWorkoutFactory.CreateClinicianPrescribedWorkout(
            minimum,
            maximum,
            maximumPower,
            TimeSpan.FromMinutes(durationMinutes));
        AddWorkout(workout);
        SelectedWorkout = workout;
        HasConfirmedClinicianPrescription = false;
        this.RaisePropertyChanged(nameof(ClinicianPrescriptionAcknowledgementText));
        Post("Clinician-prescribed session selected. Confirm the prescription limits before starting.");
    }

    [ReactiveCommand(CanExecute = nameof(canStart))]
    private void Start()
    {
        var workout = SelectedWorkout;
        if (workout is null)
        {
            Post("Choose a workout before starting training.");
            return;
        }

        if (!ushort.TryParse(dashboard.Telemetry.FunctionalThresholdPowerText, out var ftp) || ftp == 0)
        {
            Post("Enter a positive FTP in Settings before starting training.");
            return;
        }

        if (!dashboard.TryGetTrainingPowerCapabilities(out var capabilities))
        {
            Post("Connect a bike with a supported power-target range before starting.");
            return;
        }

        CompiledTrainingWorkout compiledWorkout;
        var preferredCadence = dashboard.Telemetry.PreferredCadenceRpm;
        try
        {
            compiledWorkout = TrainingWorkoutCompiler.Compile(
                workout,
                ftp,
                capabilities,
                preferredCadence,
                dashboard.HeartRate.ZoneProfile);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            Post(exception.Message);
            return;
        }

        Dispatch(new WorkoutBegun(
            DateTimeOffset.UtcNow,
            compiledWorkout,
            capabilities,
            IsRampTest: false,
            preferredCadence,
            dashboard.Telemetry.FunctionalThresholdPowerText));
    }

    private void CreateHealthWorkout(bool vigorous)
    {
        if (!dashboard.IsHeartRateConnected)
        {
            Post("Connect a heart-rate sensor before creating a heart-rate session.");
            return;
        }

        if (dashboard.HeartRate.ZoneProfile is null)
        {
            Post("Configure heart-rate zones before creating health cardio so the plan can track actual easy-zone minutes.");
            return;
        }

        if (!ushort.TryParse(dashboard.HeartRate.MaximumHeartRateText, out var maximumHeartRate)
            || !ushort.TryParse(HeartRateReserveRestingText, out var restingHeartRate)
            || !int.TryParse(HealthWorkoutDurationText, out var durationMinutes)
            || maximumHeartRate is < 80 or > 250
            || restingHeartRate is < 30 or > 200
            || maximumHeartRate - restingHeartRate < 20
            || durationMinutes is < 1 or > 240)
        {
            Post("Enter a valid maximum and resting heart rate (at least 20 bpm apart) and duration from 1 to 240 minutes.");
            return;
        }

        var workout = HeartRateWorkoutFactory.CreateGeneralHealthWorkout(
            maximumHeartRate,
            restingHeartRate,
            vigorous,
            TimeSpan.FromMinutes(durationMinutes));
        AddWorkout(workout);
        SelectedWorkout = workout;
        HasCompletedHealthScreening = false;
        this.RaisePropertyChanged(nameof(HealthScreeningAcknowledgementText));
        Post($"{workout.Name} selected. Complete the health screening before starting.");
    }

    private void AddWorkout(TrainingWorkout workout)
    {
        var existingIndex = customWorkouts.FindIndex(candidate => candidate.Id == workout.Id);
        if (existingIndex >= 0)
        {
            customWorkouts[existingIndex] = workout;
        }
        else
        {
            customWorkouts.Add(workout);
        }

        RefreshHeartRateWorkouts();
    }

    private void RefreshHeartRateWorkouts()
    {
        if (IsCatalogLoading)
        {
            return;
        }

        var selectedId = SelectedWorkout?.Id;
        IReadOnlyList<TrainingWorkout> zoneWorkouts = dashboard.IsHeartRateConnected && dashboard.HeartRate.ZoneProfile is { } profile
            ? HeartRateWorkoutFactory.CreateZoneWorkouts(profile)
            : [];
        var available = new List<TrainingWorkout>(catalogWorkouts.Count + zoneWorkouts.Count + customWorkouts.Count);
        available.AddRange(catalogWorkouts);
        available.AddRange(zoneWorkouts);
        available.AddRange(customWorkouts);
        workoutsById = available.ToDictionary(static workout => workout.Id, StringComparer.Ordinal);
        workouts.Clear();
        foreach (var workout in available)
        {
            workouts.Add(workout);
        }

        if (selectedId is not null && workoutsById.TryGetValue(selectedId, out var updatedSelection))
        {
            SelectedWorkout = updatedSelection;
        }
        else if (SelectedWorkout?.Kind == TrainingWorkoutKind.HeartRateZone)
        {
            SelectedWorkout = catalogWorkouts.FirstOrDefault();
        }

        this.RaisePropertyChanged(nameof(IsCatalogLoaded));
        this.RaisePropertyChanged(nameof(SelectedPlanWorkouts));
    }

    private void UpdateWeeklyLowIntensityText()
    {
        if (dashboard.TryGetWeeklyHeartRateZoneMinutes(out var minutes, out var error))
        {
            var goal = SelectedPlanWeek?.LowIntensityMinutes;
            WeeklyLowIntensityText = goal is int target
                ? $"Measured heart-rate Z1–Z2: {minutes}/{target} min in the last 7 days"
                : $"Measured heart-rate Z1–Z2: {minutes} min in the last 7 days";
            return;
        }

        WeeklyLowIntensityText = error;
    }

    [ReactiveCommand(CanExecute = nameof(canSelect))]
    private void SelectWorkout(TrainingWorkout workout)
    {
        if (SelectedWorkout?.Id != workout.Id)
        {
            HasCompletedHealthScreening = false;
            HasConfirmedClinicianPrescription = false;
            this.RaisePropertyChanged(nameof(HealthScreeningAcknowledgementText));
            this.RaisePropertyChanged(nameof(ClinicianPrescriptionAcknowledgementText));
        }

        SelectedWorkout = workout;
        Post(workout.Segments.Any(static segment => segment.HeartRateTarget is not null)
            ? TrainingSessionEngine.HasFreshHeartRate(ReadTelemetry(), DateTimeOffset.UtcNow)
                ? $"{workout.Name} selected. Heart-rate control adjusts trainer power gradually."
                : $"{workout.Name} selected. Connect a heart-rate sensor and wait for a live reading before starting."
            : $"{workout.Name} selected.");
    }

    [ReactiveCommand(CanExecute = nameof(canSelect))]
    private void SelectPlan(TrainingPlan plan)
    {
        SelectedPlan = plan;
        SelectedPlanWeek = plan.Weeks[0];
        Post($"{plan.Name} selected. Choose a week, then one of its workouts.");
    }

    [ReactiveCommand(CanExecute = nameof(canSelect))]
    private void SelectPlanWeek(TrainingPlanWeek week)
    {
        if (SelectedPlan is null || !SelectedPlan.Weeks.Contains(week))
        {
            return;
        }

        SelectedPlanWeek = week;
        Post($"Week {week.Number}: {week.Focus}");
    }

    [ReactiveCommand(CanExecute = nameof(canStartRampTest))]
    private void StartRampTest()
    {
        if (!dashboard.TryGetTrainingPowerCapabilities(out var capabilities))
        {
            Post("This bike does not expose a usable target-power range.");
            return;
        }

        Dispatch(new WorkoutBegun(
            DateTimeOffset.UtcNow,
            RampFtpProtocol.Create(capabilities),
            capabilities,
            IsRampTest: true,
            dashboard.Telemetry.PreferredCadenceRpm,
            dashboard.Telemetry.FunctionalThresholdPowerText));
    }

    [ReactiveCommand(CanExecute = nameof(canPause))]
    private void Pause() => Dispatch(new PauseRequested(DateTimeOffset.UtcNow));

    [ReactiveCommand(CanExecute = nameof(canResume))]
    private void Resume() => Dispatch(new ResumeRequested(DateTimeOffset.UtcNow));

    [ReactiveCommand(CanExecute = nameof(canRetry))]
    private void Retry() => Dispatch(new RetryRequested(DateTimeOffset.UtcNow));

    [ReactiveCommand(CanExecute = nameof(canEnd))]
    private void End() => Dispatch(new EndRequested(DateTimeOffset.UtcNow));

    [ReactiveCommand(CanExecute = nameof(canFinishRampTest))]
    private void FinishRampTest() => Dispatch(new RampTestFinishRequested(DateTimeOffset.UtcNow));

    [ReactiveCommand(CanExecute = nameof(canAcceptRampEstimate))]
    private async Task AcceptRampEstimateAsync(CancellationToken cancellationToken)
    {
        if (RampEstimate is not RampFtpEstimateReady estimate)
        {
            return;
        }

        var ftp = estimate.EstimatedFtpWatts;
        dashboard.Telemetry.FunctionalThresholdPowerText = ftp.ToString(CultureInfo.InvariantCulture);
        await dashboard.Telemetry.ApplyFunctionalThresholdPowerCommand.Execute().ToTask(cancellationToken);
        Post($"FTP updated to {ftp} W.");
    }

    private void Dispatch(TrainingSessionEvent sessionEvent) => events.OnNext(sessionEvent);

    private void Post(string text) => Dispatch(new NoticePosted(DateTimeOffset.UtcNow, text));

    // Interprets an engine effect against the dashboard; every outcome returns to the engine as an event.
    private IObservable<TrainingSessionEvent> Execute(TrainingSessionEffect effect) => effect switch
    {
        AddTrainingEvent add => Run(() => dashboard.AddTrainingEvent(add.Text)),
        NavigateTo navigate => Run(() => dashboard.RequestNavigation(ToDashboardDestination(navigate.Destination))),
        ResetSafetyAcknowledgements => Run(ResetAcknowledgements),
        RecordHeartRateVolume record => Observable.Defer(() => Observable.Return<TrainingSessionEvent>(RecordVolume(record))),
        StartMachineSession start => Invoke(
            async cancellationToken =>
            {
                var accepted = await dashboard.StartTrainingSessionAsync(cancellationToken);
                return new MachineSessionStarted(DateTimeOffset.UtcNow, start, accepted, dashboard.ConnectionStatus);
            },
            () => new MachineSessionStarted(DateTimeOffset.UtcNow, start, false, dashboard.ConnectionStatus)),
        PauseMachineSession pause => Invoke(
            async cancellationToken => new MachineSessionPaused(DateTimeOffset.UtcNow, pause, await dashboard.PauseTrainingSessionAsync(cancellationToken)),
            () => new MachineSessionPaused(DateTimeOffset.UtcNow, pause, false)),
        ResumeMachineSession => Invoke(
            async cancellationToken => new MachineSessionResumed(DateTimeOffset.UtcNow, await dashboard.StartTrainingSessionAsync(cancellationToken)),
            static () => new MachineSessionResumed(DateTimeOffset.UtcNow, false)),
        EndMachineSession end => Invoke(
            async _ => new MachineSessionEnded(DateTimeOffset.UtcNow, end, await EndMachineSessionAsync(end.Purpose)),
            () => new MachineSessionEnded(DateTimeOffset.UtcNow, end, false)),
        ApplyPowerTarget target => Invoke(
            async cancellationToken => new PowerTargetCompleted(DateTimeOffset.UtcNow, target, await dashboard.ApplyTrainingTargetAsync(target.Watts, cancellationToken)),
            () => new PowerTargetCompleted(DateTimeOffset.UtcNow, target, false)),
        _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, "Unknown training effect."),
    };

    private static IObservable<TrainingSessionEvent> Run(Action action) =>
        Observable.Defer(() =>
        {
            action();
            return Observable.Empty<TrainingSessionEvent>();
        });

    // A faulted operation reports the rejected outcome first so the engine leaves its pending state, then the error.
    private static IObservable<TrainingSessionEvent> Invoke(
        Func<CancellationToken, Task<TrainingSessionEvent>> operation,
        Func<TrainingSessionEvent> rejected) =>
        Observable.FromAsync(operation)
            .Catch<TrainingSessionEvent, Exception>(exception => Observable.Return(rejected())
                .Append(new OperationFailed(DateTimeOffset.UtcNow, $"Training operation failed: {exception.Message}")));

    private async Task<bool> EndMachineSessionAsync(TrainingEndPurpose purpose)
    {
        if (!dashboard.IsConnected
            || dashboard.SessionState is not (DashboardViewModel.WorkoutSessionState.Running or DashboardViewModel.WorkoutSessionState.Paused))
        {
            return true;
        }

        var ended = await dashboard.EndTrainingSessionAsync(CancellationToken.None);
        return ended
            || purpose != TrainingEndPurpose.Rider
            && (!dashboard.IsConnected || dashboard.SessionState == DashboardViewModel.WorkoutSessionState.Stopped);
    }

    private HeartRateVolumeRecorded RecordVolume(RecordHeartRateVolume request)
    {
        var now = DateTimeOffset.UtcNow;
        if (dashboard.HeartRate.ZoneProfile is null)
        {
            return new HeartRateVolumeRecorded(now, request, false, string.Empty);
        }

        if (!dashboard.TryRecordHeartRateTrainingProgress(
            dashboard.GetRecordedSamplesSince(request.StartedAt),
            request.StartedAt,
            now,
            out var easyDuration,
            out var error))
        {
            return new HeartRateVolumeRecorded(now, request, false, error);
        }

        var easyMinutes = (int)(easyDuration.Ticks / TimeSpan.TicksPerMinute);
        var text = $"{easyMinutes} min of measured heart-rate Z1–Z2 counted toward this week's easy-volume goal.";
        dashboard.AddTrainingEvent(text);
        UpdateWeeklyLowIntensityText();
        return new HeartRateVolumeRecorded(now, request, true, text);
    }

    private void ResetAcknowledgements()
    {
        HasCompletedHealthScreening = false;
        HasConfirmedClinicianPrescription = false;
        this.RaisePropertyChanged(nameof(HealthScreeningAcknowledgementText));
        this.RaisePropertyChanged(nameof(ClinicianPrescriptionAcknowledgementText));
    }

    private TrainingTelemetry ReadTelemetry()
    {
        var telemetry = dashboard.Telemetry;
        var heartRate = dashboard.HeartRate;
        return new TrainingTelemetry(
            telemetry.IsStale ? null : telemetry.CurrentCadenceRpm,
            telemetry.IsStale ? null : telemetry.CurrentPowerWatts,
            dashboard.IsHeartRateConnected && !heartRate.IsStale ? heartRate.LatestObservation : null,
            heartRate.SafetyCeilingHeartRate);
    }

    private static TrainingMachineState ToMachineState(DashboardViewModel.WorkoutSessionState state) => state switch
    {
        DashboardViewModel.WorkoutSessionState.Running => TrainingMachineState.Running,
        DashboardViewModel.WorkoutSessionState.Paused => TrainingMachineState.Paused,
        DashboardViewModel.WorkoutSessionState.Stopped => TrainingMachineState.Stopped,
        _ => TrainingMachineState.NotStarted,
    };

    private static DashboardDestination ToDashboardDestination(TrainingDestination destination) => destination switch
    {
        TrainingDestination.Ride => DashboardDestination.Ride,
        TrainingDestination.Training => DashboardDestination.Training,
        TrainingDestination.Results => DashboardDestination.Results,
        _ => DashboardDestination.Today,
    };

    private static string GetIntervalGuidance(
        TrainingSession session,
        TrainingTelemetry telemetry,
        HeartRateObservation? heartRate,
        DateTimeOffset now)
    {
        if (session.Runner is not { CurrentSegment: { } segment } runner)
        {
            return string.Empty;
        }

        if (session.IsWaitingForPedalling)
        {
            return TrainingSessionText.WaitingForPedalling;
        }

        switch (runner.State)
        {
            case TrainingWorkoutState.ApplyingTarget:
                return "Applying ERG target...";
            case TrainingWorkoutState.Paused:
                return "Training paused.";
            case not TrainingWorkoutState.Running:
                return segment.Definition.Cue;
        }

        if (session.IsStalled)
        {
            return TrainingSessionText.Stalled;
        }

        if (segment is { TargetMinimumHeartRate: ushort minimumHeartRate, TargetMaximumHeartRate: ushort maximumHeartRate })
        {
            var heartRateText = heartRate is { } observation
                ? $"Current heart rate {observation.BeatsPerMinute} bpm."
                : "Waiting for heart rate.";
            return $"{heartRateText} Target {minimumHeartRate}-{maximumHeartRate} bpm; power adjusts gradually within set limits.";
        }

        if (session.GetSegmentElapsed(now) is not { } elapsed)
        {
            return segment.Definition.Cue;
        }

        var rampEnd = session.ActiveRampEndElapsed;
        if (elapsed < rampEnd)
        {
            return $"Ramping toward {segment.TargetWatts} W. Keep cadence steady.";
        }

        var segmentIndex = runner.SegmentIndex;
        if (elapsed < rampEnd + session.GetSettlingDuration(segmentIndex)
            && (session.IsCadenceTransition(segmentIndex) || session.ChangesPowerTarget(segmentIndex)))
        {
            return session.IsCadenceTransition(segmentIndex)
                ? "Move cadence smoothly into the new range; the trainer holds power by changing resistance."
                : "Keep cadence steady while the trainer adjusts resistance.";
        }

        var floor = session.ResistanceFloor;
        if (telemetry.CadenceRpm is double liveCadence
            && floor.IsAboveTarget(segment.TargetWatts, liveCadence)
            && floor.EstimateMinimumWatts(liveCadence) is int minimumWatts)
        {
            var floorText = $"The bike's lightest resistance is about {minimumWatts} W at {liveCadence:F0} rpm, so power reads above the {segment.TargetWatts} W target.";
            return floor.GetClosestCadence(segment.TargetWatts, segment.Definition.MinimumCadenceRpm) is int closestCadence
                && closestCadence <= liveCadence - ClosestCadenceHintRpm
                ? $"{floorText} Ride near {closestCadence} rpm to get closer."
                : $"{segment.Definition.Cue} {floorText}";
        }

        return segment.Definition.Cue;
    }

    private TimeSpan GetWorkoutTotal()
    {
        var total = TimeSpan.Zero;
        foreach (var segment in ActiveSegments)
        {
            total += segment.Duration;
        }

        return total;
    }

    private TimeSpan GetWorkoutElapsed(DateTimeOffset timestamp)
    {
        if (Runner is not { } runner)
        {
            return TimeSpan.Zero;
        }

        var segments = ActiveSegments;
        var elapsed = TimeSpan.Zero;
        var completedCount = int.Min(runner.SegmentIndex, segments.Count);
        for (var index = 0; index < completedCount; index++)
        {
            elapsed += segments[index].Duration;
        }

        return elapsed + (Session.GetSegmentElapsed(timestamp) ?? TimeSpan.Zero);
    }

    private void NotifyClockChanged()
    {
        this.RaisePropertyChanged(nameof(CurrentIntervalRemainingText));
        this.RaisePropertyChanged(nameof(IntervalGuidanceText));
        this.RaisePropertyChanged(nameof(WorkoutProgress));
        this.RaisePropertyChanged(nameof(WorkoutElapsedText));
        this.RaisePropertyChanged(nameof(WorkoutRemainingText));
    }

    private void NotifySessionChanged()
    {
        this.RaisePropertyChanged(nameof(RunnerState));
        this.RaisePropertyChanged(nameof(IsActive));
        this.RaisePropertyChanged(nameof(IsStarting));
        this.RaisePropertyChanged(nameof(StatusText));
        this.RaisePropertyChanged(nameof(AdherenceText));
        this.RaisePropertyChanged(nameof(CadenceCalibrationText));
        this.RaisePropertyChanged(nameof(RampEstimate));
        this.RaisePropertyChanged(nameof(RampEstimateText));
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
        this.RaisePropertyChanged(nameof(NextSegmentText));
        this.RaisePropertyChanged(nameof(PowerCadenceTargetWatts));
        this.RaisePropertyChanged(nameof(PowerCadenceMinimumCadenceRpm));
        this.RaisePropertyChanged(nameof(PowerCadenceMaximumCadenceRpm));
        this.RaisePropertyChanged(nameof(PowerCadenceAxisMinimumCadenceRpm));
        this.RaisePropertyChanged(nameof(PowerCadenceAxisMaximumCadenceRpm));
        this.RaisePropertyChanged(nameof(PowerCadenceAxisMaximumWatts));
        this.RaisePropertyChanged(nameof(CadenceTargetText));
        NotifyClockChanged();
    }

    private static string FormatRemainingTime(TimeSpan remaining)
    {
        var seconds = (int)double.Ceiling(remaining.TotalSeconds);
        return $"{seconds / 60:D2}:{seconds % 60:D2}";
    }

    private static string GetTargetDescription(CompiledTrainingSegment segment) =>
        segment.TargetMinimumHeartRate is ushort minimumHeartRate
            && segment.TargetMaximumHeartRate is ushort maximumHeartRate
                ? $"{minimumHeartRate}-{maximumHeartRate} bpm"
                : $"{segment.TargetWatts} W";
}