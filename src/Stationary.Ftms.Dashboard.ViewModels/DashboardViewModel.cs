using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using Stationary.Ftms;
using Stationary.Ftms.Dashboard.Core;
using Stationary.Ftms.Dashboard.Services;

namespace Stationary.Ftms.Dashboard.ViewModels;

public enum DashboardDestination
{
    Today,
    Ride,
    Training,
    Results,
    Settings,
    Sensors,
    HeartRateSensor,
}

public enum RideSessionMode
{
    OpenRide,
    StructuredTraining,
}

public sealed partial class DashboardViewModel : ReactiveObject, IAsyncDisposable
{
    private const byte StopControlInformation = 0x01;
    private const byte PauseControlInformation = 0x02;
    private const int SessionArchiveFormatVersion = 1;
    private const int MaximumRecordedSamples = 86_400;
    private static readonly TimeSpan ChartWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RecordedValueFreshness = TimeSpan.FromSeconds(5);

    private readonly IFtmsDiscoveryService discoveryService;
    private readonly IHeartRateDiscoveryService heartRateDiscoveryService;
    private readonly IScheduler timerScheduler;
    private readonly IDashboardPlatform platform;
    private readonly IObservable<bool> canScan;
    private readonly IObservable<bool> canConnect;
    private readonly IObservable<bool> canHeartRateConnect;
    private readonly IObservable<bool> canDisconnect;
    private readonly IObservable<bool> canHeartRateDisconnect;
    private readonly IObservable<bool> canResetSession;
    private readonly IObservable<bool> canRequestControl;
    private readonly IObservable<bool> canStart;
    private readonly IObservable<bool> canStop;
    private readonly IObservable<bool> canAddLap;
    private readonly IObservable<bool> canStartFreeRide;
    private readonly IObservable<bool> canEndFreeRide;
    private readonly SerialDisposable sessionSubscriptions = new();
    private readonly SerialDisposable scanSubscriptions = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly ISubject<SessionTelemetry> telemetryIngress = Subject.Synchronize(new Subject<SessionTelemetry>());
    private readonly IObservable<TelemetrySnapshot> currentSessionTelemetry;
    private readonly IObservable<bool> canApplyTarget;
    private readonly ISubject<Unit> telemetrySessionReset = Subject.Synchronize(new Subject<Unit>());
    private readonly ISubject<SessionTermination> sessionTerminationIngress = Subject.Synchronize(new Subject<SessionTermination>());
    private readonly SerialDisposable heartRateSessionSubscriptions = new();
    private readonly SerialDisposable heartRateScanSubscriptions = new();
    private readonly ISubject<HeartRateSessionTelemetry> heartRateIngress = Subject.Synchronize(new Subject<HeartRateSessionTelemetry>());
    private readonly IObservable<HeartRateObservation> currentHeartRateTelemetry;
    private readonly ISubject<Unit> heartRateSessionReset = Subject.Synchronize(new Subject<Unit>());
    private readonly ISubject<HeartRateSessionTermination> heartRateSessionTerminationIngress = Subject.Synchronize(new Subject<HeartRateSessionTermination>());
    private readonly ISubject<DashboardDestination> navigationRequests = Subject.Synchronize(new Subject<DashboardDestination>());
    private readonly ISubject<Unit> sheetDismissRequests = Subject.Synchronize(new Subject<Unit>());
    private readonly CompositeDisposable subscriptions = [];
    private readonly Queue<RecordedTelemetrySample> recordedSamples = [];
    private PhysicalControlSynchronizationState physicalControlSynchronizationState;
    private bool isConnected;
    private bool isHeartRateConnected;
    private bool isControllable;
    private bool hasControlPermission;
    private bool isWorkoutActive;
    private WorkoutSessionState workoutSessionState;
    private bool isSessionRecording;
    private TargetControlViewModel? powerTargetControl;
    private IFtmsSession? session;
    private IHeartRateSession? heartRateSession;
    private CancellationTokenSource? telemetryCancellation;
    private Task? telemetryConsumer;
    private CancellationTokenSource? heartRateTelemetryCancellation;
    private Task? heartRateTelemetryConsumer;
    private CancellationTokenSource? scanCancellation;
    private Task? scanConsumer;
    private CancellationTokenSource? heartRateScanCancellation;
    private Task? heartRateScanConsumer;
    private DateTimeOffset? chartSelectionStart;
    private DateTimeOffset? chartSelectionEnd;
    private bool isCustomChartRange;
    private int lapCount;
    private TelemetrySnapshot? latestRecordedTelemetry;
    private HeartRateObservation? latestRecordedHeartRate;
    private DateTimeOffset? lastRecordedAt;
    private RecordedSession? previousSession;
    private DateTimeOffset? lastWorkoutAnalysisAt;
    private int isDisposed;

    public enum WorkoutSessionState
    {
        NotStarted,
        Running,
        Paused,
        Stopped,
    }

    private enum PhysicalControlSynchronizationState
    {
        Unavailable,
        Listening,
        Synchronized,
    }

    public DashboardViewModel(IFtmsDiscoveryService discoveryService, IHeartRateDiscoveryService heartRateDiscoveryService, IDashboardPlatform platform)
        : this(discoveryService, heartRateDiscoveryService, platform, RxApp.TaskpoolScheduler)
    {
    }

    internal DashboardViewModel(IFtmsDiscoveryService discoveryService, IHeartRateDiscoveryService heartRateDiscoveryService, IDashboardPlatform platform, IScheduler timerScheduler)
    {
        this.discoveryService = discoveryService;
        this.heartRateDiscoveryService = heartRateDiscoveryService;
        this.timerScheduler = timerScheduler;
        this.platform = platform;
        currentSessionTelemetry = telemetryIngress
            .Where(telemetry => ReferenceEquals(session, telemetry.Session))
            .Select(static telemetry => telemetry.Snapshot);
        currentHeartRateTelemetry = heartRateIngress
            .Where(telemetry => ReferenceEquals(heartRateSession, telemetry.Session))
            .Select(static telemetry => telemetry.Observation);
        DiscoveredDevices = [];
        HeartRateDiscoveredDevices = [];
        DeviceCapabilities = [];
        LiveCapabilities = [];
        AvailableControlCapabilities = [];
        UnavailableCapabilities = [];
        Telemetry = new(platform);
        HeartRate = new(platform);
        Training = new(this);
        subscriptions.Add(Training.WhenAnyValue(viewModel => viewModel.IsActive)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsTrainingActive));
                this.RaisePropertyChanged(nameof(IsOpenRide));
                this.RaisePropertyChanged(nameof(HasOpenRideControls));
                this.RaisePropertyChanged(nameof(RideSessionMode));
                this.RaisePropertyChanged(nameof(RideTitle));
                this.RaisePropertyChanged(nameof(RideSubtitle));
            }));
        TargetControls = [];
        ManualTargetControls = [];
        var sampledTelemetry = telemetrySessionReset
            .StartWith(Unit.Default)
            .Select(_ => currentSessionTelemetry
                .Sample(TimeSpan.FromMilliseconds(200), timerScheduler)
                .Select<TelemetrySnapshot, TelemetrySnapshot?>(static snapshot => snapshot)
                .StartWith((TelemetrySnapshot?)null))
            .Switch()
            .Publish()
            .RefCount();
        subscriptions.Add(sampledTelemetry
            .Select(snapshot => Observable.Return(snapshot, RxApp.MainThreadScheduler))
            .Switch()
            .Subscribe(snapshot =>
            {
                if (snapshot is { } current)
                {
                    Present(current);
                }
                else
                {
                    ClearTelemetryPresentation();
                }
            }, HandleTelemetryPipelineError));
        subscriptions.Add(telemetrySessionReset
            .StartWith(Unit.Default)
            .Select(_ => currentSessionTelemetry
                .Sample(TimeSpan.FromSeconds(1), timerScheduler)
                .Scan(new TelemetryChartHistory(), static (history, snapshot) => history.Add(snapshot))
                .Select<TelemetryChartHistory, TelemetryChartWindow?>(static history => history.Snapshot())
                .StartWith((TelemetryChartWindow?)null))
            .Switch()
            .Select(chart => Observable.Return(chart, RxApp.MainThreadScheduler))
            .Switch()
            .Subscribe(chart =>
            {
                Telemetry.SetChartSamples(chart);
                UpdateChartTimeRange();
            }, HandleTelemetryPipelineError));
        subscriptions.Add(telemetrySessionReset
            .StartWith(Unit.Default)
            .Select(_ => currentSessionTelemetry.Publish(shared => TelemetryFieldStreams.Definitions
                .Select(definition => shared.ObserveField(definition))
                .Merge()))
            .Switch()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ApplyTelemetryFieldUpdate, HandleTelemetryPipelineError));
        subscriptions.Add(heartRateSessionReset
            .StartWith(Unit.Default)
            .Select(_ => currentHeartRateTelemetry
                .Sample(TimeSpan.FromSeconds(1), timerScheduler)
                .Select<HeartRateObservation, HeartRateObservation?>(static observation => observation)
                .StartWith((HeartRateObservation?)null))
            .Switch()
            .Select(observation => Observable.Return(observation, RxApp.MainThreadScheduler))
            .Switch()
            .Subscribe(observation =>
            {
                if (observation is { } current)
                {
                    PresentHeartRate(current);
                }
                else
                {
                    HeartRate.Clear();
                }

                UpdateChartTimeRange();
            }, HandleHeartRatePipelineError));
        canScan = this.WhenAnyValue(
                viewModel => viewModel.IsScanning,
                viewModel => viewModel.IsHeartRateScanning,
                (scanning, heartRateScanning) => !scanning && !heartRateScanning)
            .ObserveOn(RxApp.MainThreadScheduler);
        canConnect = this.WhenAnyValue(
                viewModel => viewModel.SelectedDevice,
                viewModel => viewModel.IsConnected,
                viewModel => viewModel.IsHeartRateScanning,
                (device, connected, heartRateScanning) => device is not null && !connected && !heartRateScanning)
            .ObserveOn(RxApp.MainThreadScheduler);
        canHeartRateConnect = this.WhenAnyValue(
                viewModel => viewModel.SelectedHeartRateDevice,
                viewModel => viewModel.IsHeartRateConnected,
                viewModel => viewModel.IsScanning,
                (device, connected, scanning) => device is not null && !connected && !scanning)
            .ObserveOn(RxApp.MainThreadScheduler);
        canDisconnect = this.WhenAnyValue(viewModel => viewModel.IsConnected).ObserveOn(RxApp.MainThreadScheduler);
        canHeartRateDisconnect = this.WhenAnyValue(viewModel => viewModel.IsHeartRateConnected).ObserveOn(RxApp.MainThreadScheduler);
        canResetSession = this.WhenAnyValue(viewModel => viewModel.IsConnected, viewModel => viewModel.IsHeartRateConnected, (fitnessMachineConnected, heartRateConnected) => fitnessMachineConnected || heartRateConnected).ObserveOn(RxApp.MainThreadScheduler);
        canRequestControl = this.WhenAnyValue(viewModel => viewModel.IsControllable, viewModel => viewModel.HasControlPermission, (controllable, hasPermission) => controllable && !hasPermission).ObserveOn(RxApp.MainThreadScheduler);
        canStart = this.WhenAnyValue(viewModel => viewModel.IsControllable, viewModel => viewModel.IsWorkoutActive, (controllable, active) => controllable && !active).ObserveOn(RxApp.MainThreadScheduler);
        canStop = this.WhenAnyValue(viewModel => viewModel.IsControllable, viewModel => viewModel.IsWorkoutActive, (controllable, active) => controllable && active).ObserveOn(RxApp.MainThreadScheduler);
        canAddLap = this.WhenAnyValue(viewModel => viewModel.IsWorkoutActive).ObserveOn(RxApp.MainThreadScheduler);
        canApplyTarget = this.WhenAnyValue(viewModel => viewModel.IsControllable).ObserveOn(RxApp.MainThreadScheduler);
        canStartFreeRide = this.WhenAnyValue(
                viewModel => viewModel.IsSessionAvailable,
                viewModel => viewModel.Training.IsActive,
                (available, training) => available && !training)
            .ObserveOn(RxApp.MainThreadScheduler);
        canEndFreeRide = this.WhenAnyValue(viewModel => viewModel.Training.IsActive).Select(static training => !training).ObserveOn(RxApp.MainThreadScheduler);
        subscriptions.Add(LoadPreviousSessionCommand
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ApplyPreviousSessionLoadResult));
        subscriptions.Add(LoadPreviousSessionCommand.ThrownExceptions
            .ObserveOn(RxApp.MainThreadScheduler)
            .Where(exception => exception is not OperationCanceledException)
            .Subscribe(exception =>
            {
                PreviousSessionComparisonText = "Previous session data is unavailable.";
                ConnectionStatus = $"Previous session data could not be loaded: {exception.Message}";
            }));
        subscriptions.Add(LoadPreviousSessionCommand.Execute().Subscribe());
        var sessionTerminationExecuting = sessionTerminationIngress
            .ObserveOn(RxApp.MainThreadScheduler)
            .Select(termination => Observable
                .FromAsync(() => HandleSessionTerminationAsync(termination))
                .Select(static _ => false)
                .StartWith(true)
                .Catch<bool, Exception>(exception =>
                {
                    HandleTelemetryPipelineError(exception);
                    return Observable.Return(false);
                }))
            .Concat()
            .StartWith(false)
            .Replay(1)
            .RefCount();
        var heartRateSessionTerminationExecuting = heartRateSessionTerminationIngress
            .ObserveOn(RxApp.MainThreadScheduler)
            .Select(termination => Observable
                .FromAsync(() => HandleHeartRateSessionTerminationAsync(termination))
                .Select(static _ => false)
                .StartWith(true)
                .Catch<bool, Exception>(exception =>
                {
                    HandleHeartRatePipelineError(exception);
                    return Observable.Return(false);
                }))
            .Concat()
            .StartWith(false)
            .Replay(1)
            .RefCount();
        var targetOperationExecuting = Observable
            .FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                handler => TargetControls.CollectionChanged += handler,
                handler => TargetControls.CollectionChanged -= handler)
            .Select(_ => TargetControls.Select(control => control.ApplyCommand.IsExecuting))
            .StartWith(TargetControls.Select(control => control.ApplyCommand.IsExecuting))
            .Select(executions => executions.Any()
                ? executions.CombineLatest().Select(states => states.Any(executing => executing))
                : Observable.Return(false))
            .Switch()
            .DistinctUntilChanged();
        _isBusyHelper = Observable.CombineLatest(
                ScanCommand.IsExecuting,
                ConnectCommand.IsExecuting,
                DisconnectCommand.IsExecuting,
                RequestControlCommand.IsExecuting,
                StartCommand.IsExecuting,
                StopCommand.IsExecuting,
                StartFreeRideCommand.IsExecuting,
                EndFreeRideCommand.IsExecuting,
                SetTargetInclinationCommand.IsExecuting,
                SetTargetPowerCommand.IsExecuting,
                SetTargetResistanceCommand.IsExecuting,
                ExportSessionCommand.IsExecuting,
                sessionTerminationExecuting,
                heartRateSessionTerminationExecuting,
                targetOperationExecuting)
            .Select(states => states.Any(static executing => executing))
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.IsBusy);
        _canInitiateOperationHelper = this.WhenAnyValue(viewModel => viewModel.IsBusy)
            .Select(busy => !busy)
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.CanInitiateOperation);
        _operationStatusHelper = Observable.CombineLatest(
                ScanCommand.IsExecuting.Select(executing => executing ? "Searching for FTMS fitness machines..." : null),
                ConnectCommand.IsExecuting.Select(executing => executing ? "Connecting to the fitness machine..." : null),
                DisconnectCommand.IsExecuting.Select(executing => executing ? "Disconnecting from the fitness machine..." : null),
                RequestControlCommand.IsExecuting.Select(executing => executing ? "Requesting control from the fitness machine..." : null),
                StartCommand.IsExecuting.Select(executing => executing ? "Starting the fitness machine session..." : null),
                StopCommand.IsExecuting.Select(executing => executing ? "Pausing the fitness machine session..." : null),
                StartFreeRideCommand.IsExecuting.Select(executing => executing ? "Starting the free ride..." : null),
                EndFreeRideCommand.IsExecuting.Select(executing => executing ? "Ending the ride..." : null),
                SetTargetInclinationCommand.IsExecuting.Select(executing => executing ? "Applying target inclination..." : null),
                SetTargetPowerCommand.IsExecuting.Select(executing => executing ? "Applying target power..." : null),
                SetTargetResistanceCommand.IsExecuting.Select(executing => executing ? "Applying target resistance..." : null),
                ExportSessionCommand.IsExecuting.Select(executing => executing ? "Exporting session summary..." : null),
                sessionTerminationExecuting.Select(executing => executing ? "Cleaning up the disconnected fitness machine..." : null),
                heartRateSessionTerminationExecuting.Select(executing => executing ? "Cleaning up the disconnected heart-rate sensor..." : null),
                targetOperationExecuting.Select(executing => executing ? "Applying fitness-machine target..." : null))
            .Select(statuses => statuses.FirstOrDefault(static status => status is not null) ?? string.Empty)
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, viewModel => viewModel.OperationStatus);
        _statusTextHelper = this.WhenAnyValue(
                viewModel => viewModel.ConnectionStatus,
                viewModel => viewModel.OperationStatus,
                (connection, operation) => string.IsNullOrEmpty(operation) ? connection : operation)
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.StatusText);

        _isControlOperationExecutingHelper = Observable.CombineLatest(
                RequestControlCommand.IsExecuting,
                StartCommand.IsExecuting,
                StopCommand.IsExecuting)
            .Select(states => states.Any(static executing => executing))
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.IsControlOperationExecuting);
        _isWorkingHelper = this.WhenAnyValue(
            viewModel => viewModel.IsBusy,
            viewModel => viewModel.IsScanning,
            viewModel => viewModel.IsHeartRateScanning,
            (busy, scanning, heartRateScanning) => busy || scanning || heartRateScanning)
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.IsWorking);
        subscriptions.Add(Observable.Merge(
                ScanCommand.ThrownExceptions,
                ConnectCommand.ThrownExceptions,
                DisconnectCommand.ThrownExceptions,
                RequestControlCommand.ThrownExceptions,
                StartCommand.ThrownExceptions,
                StopCommand.ThrownExceptions,
                StartFreeRideCommand.ThrownExceptions,
                EndFreeRideCommand.ThrownExceptions,
                SetTargetInclinationCommand.ThrownExceptions,
                SetTargetPowerCommand.ThrownExceptions,
                SetTargetResistanceCommand.ThrownExceptions)
            .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => ConnectionStatus = "The requested operation failed. Reconnect and try again.", HandleTelemetryPipelineError));
        subscriptions.Add(Observable.Merge(
                ScanHeartRateCommand.ThrownExceptions,
                ConnectHeartRateCommand.ThrownExceptions,
                DisconnectHeartRateCommand.ThrownExceptions)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => HeartRateConnectionStatus = "The heart-rate sensor operation failed. Scan and reconnect.", HandleHeartRatePipelineError));
        subscriptions.Add(Observable
            .FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                handler => TargetControls.CollectionChanged += handler,
                handler => TargetControls.CollectionChanged -= handler)
            .Select(_ => TargetControls.Select(control => control.ApplyCommand.ThrownExceptions).Merge())
            .StartWith(TargetControls.Select(control => control.ApplyCommand.ThrownExceptions).Merge())
            .Switch()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => ConnectionStatus = "The target could not be applied. Reconnect and try again.", HandleTelemetryPipelineError));
        subscriptions.Add(this.WhenAnyValue(viewModel => viewModel.IsConnected)
            .Select(connected => connected && session is { } activeSession
                ? ObserveTelemetrySilence(activeSession)
                : Observable.Empty<IFtmsSession>())
            .Switch()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Where(activeSession => IsConnected && ReferenceEquals(session, activeSession))
            .Subscribe(_ =>
            {
                Telemetry.MarkStale();
                ConnectionStatus = "Telemetry has stopped arriving. The connection may be stale.";
            }, HandleTelemetryPipelineError));
        subscriptions.Add(this.WhenAnyValue(viewModel => viewModel.IsHeartRateConnected)
            .Select(connected => connected && heartRateSession is { } activeSession
                ? ObserveHeartRateSilence(activeSession)
                : Observable.Empty<IHeartRateSession>())
            .Switch()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Where(activeSession => IsHeartRateConnected && ReferenceEquals(heartRateSession, activeSession))
            .Subscribe(_ =>
            {
                HeartRate.MarkStale();
                HeartRateConnectionStatus = "Heart-rate updates have stopped. The sensor may be stale.";
            }, HandleHeartRatePipelineError));
        _machineChipTextHelper = this.WhenAnyValue(viewModel => viewModel.IsConnected, viewModel => viewModel.MachineName)
            .Select(static state => state.Item1 ? state.Item2 ?? "Fitness machine" : "No machine")
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.MachineChipText, "No machine");
        _machineChipDetailHelper = this.WhenAnyValue(
                viewModel => viewModel.IsConnected,
                viewModel => viewModel.IsControllable,
                viewModel => viewModel.PowerTargetControl,
                static (connected, controllable, power) => !connected
                    ? "Connect"
                    : power is { } target
                        ? string.Create(CultureInfo.InvariantCulture, $"ERG {target.Minimum:F0}-{target.Maximum:F0} W")
                        : controllable ? "Controls without ERG" : "Telemetry only")
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.MachineChipDetail, "Connect");
        _heartRateChipTextHelper = this.WhenAnyValue(
                viewModel => viewModel.IsHeartRateConnected,
                viewModel => viewModel.HeartRate.CurrentHeartRateText,
                static (connected, heartRate) => connected ? heartRate : "No HR sensor")
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.HeartRateChipText, "No HR sensor");
        _heartRateChipDetailHelper = this.WhenAnyValue(
                viewModel => viewModel.IsHeartRateConnected,
                viewModel => viewModel.HeartRate.CurrentZoneText,
                static (connected, zone) => connected ? zone : "Optional")
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.HeartRateChipDetail, "Optional");
        var functionalThresholdPower = this.WhenAnyValue(viewModel => viewModel.Telemetry.FunctionalThresholdPowerText)
            .Select(static text => ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var watts) && watts > 0 ? watts : (ushort)0)
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount();
        _isFtpSetHelper = functionalThresholdPower
            .Select(static watts => watts > 0)
            .ToProperty(this, viewModel => viewModel.IsFtpSet);
        _ftpChipTextHelper = functionalThresholdPower
            .Select(static watts => watts > 0 ? string.Create(CultureInfo.InvariantCulture, $"{watts} W") : "Not set")
            .ToProperty(this, viewModel => viewModel.FtpChipText, "Not set");
        var readiness = Observable.CombineLatest(
                this.WhenAnyValue(viewModel => viewModel.IsConnected),
                this.WhenAnyValue(viewModel => viewModel.PowerTargetControl).Select(static power => power is not null),
                functionalThresholdPower,
                static (connected, hasPowerTarget, ftp) => (connected, hasPowerTarget, ftp))
            .Replay(1)
            .RefCount();
        _isWorkoutReadyHelper = readiness
            .Select(static state => state.connected && state.hasPowerTarget && state.ftp > 0)
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.IsWorkoutReady);
        _workoutReadinessTextHelper = readiness
            .Select(static state => !state.connected
                ? "Connect a fitness machine to start a workout."
                : !state.hasPowerTarget
                    ? "This machine does not accept power targets. Use free ride."
                    : state.ftp == 0
                        ? "Set your FTP so workout targets match your fitness."
                        : string.Create(CultureInfo.InvariantCulture, $"Ready. Targets scale to your {state.ftp} W FTP."))
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.WorkoutReadinessText, "Connect a fitness machine to start a workout.");
        _isRideInProgressHelper = this.WhenAnyValue(
                viewModel => viewModel.IsWorkoutActive,
                viewModel => viewModel.Training.IsActive,
                static (workoutActive, trainingActive) => workoutActive || trainingActive)
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.IsRideInProgress);
    }

    [Reactive]
    public partial string ConnectionStatus { get; private set; } = "No fitness machine connected";

    [Reactive]
    public partial string HeartRateConnectionStatus { get; private set; } = "No external heart-rate sensor connected";

    [Reactive]
    public partial string ControlStatus { get; private set; } = "Connect a fitness machine to inspect FTMS controls.";

    public ObservableCollection<FtmsDiscoveredDevice> DiscoveredDevices { get; }

    public ObservableCollection<HeartRateDiscoveredDevice> HeartRateDiscoveredDevices { get; }

    public ObservableCollection<DeviceCapability> DeviceCapabilities { get; }

    public ObservableCollection<DeviceCapability> LiveCapabilities { get; }

    public ObservableCollection<DeviceCapability> AvailableControlCapabilities { get; }

    public ObservableCollection<DeviceCapability> UnavailableCapabilities { get; }

    public TelemetryPresentationViewModel Telemetry { get; }

    public HeartRatePresentationViewModel HeartRate { get; }

    [Reactive]
    public partial DateTimeOffset? ChartTimeRangeStart { get; private set; }

    [Reactive]
    public partial DateTimeOffset? ChartTimeRangeEnd { get; private set; }

    [Reactive]
    public partial DateTimeOffset? ChartHoverTimestamp { get; set; }

    [Reactive]
    public partial IReadOnlyList<SessionEventMarker> SessionEvents { get; private set; } = [];

    [Reactive]
    public partial IReadOnlyList<TelemetrySample> PreviousPowerChartSamples { get; private set; } = [];

    [Reactive]
    public partial IReadOnlyList<TelemetrySample> PreviousSpeedChartSamples { get; private set; } = [];

    [Reactive]
    public partial IReadOnlyList<TelemetrySample> PreviousCadenceChartSamples { get; private set; } = [];

    [Reactive]
    public partial IReadOnlyList<TelemetrySample> PreviousHeartRateChartSamples { get; private set; } = [];

    public DateTimeOffset? ChartSelectionStart
    {
        get => chartSelectionStart;
        set
        {
            if (chartSelectionStart != value)
            {
                this.RaiseAndSetIfChanged(ref chartSelectionStart, value);
                UpdateSelectedIntervalSummary();
            }
        }
    }

    public DateTimeOffset? ChartSelectionEnd
    {
        get => chartSelectionEnd;
        set
        {
            if (chartSelectionEnd != value)
            {
                this.RaiseAndSetIfChanged(ref chartSelectionEnd, value);
                UpdateSelectedIntervalSummary();
            }
        }
    }

    [Reactive]
    public partial string SelectedIntervalSummary { get; private set; } = "Drag across a chart to inspect an interval.";

    [Reactive]
    public partial string PreviousSessionComparisonText { get; private set; } = "No previous session saved.";

    [Reactive]
    public partial string EfficiencyText { get; private set; } = "Gathering paired data";

    [Reactive]
    public partial string DecouplingText { get; private set; } = "Requires 10 minutes";

    [Reactive]
    public partial string CadenceAnalysisText { get; private set; } = "Gathering cadence";

    [Reactive]
    public partial string TorqueExposureText { get; private set; } = "--";

    [Reactive]
    public partial string RecoveryText { get; private set; } = "Waiting for recovery";

    public ObservableCollection<TargetControlViewModel> TargetControls { get; }

    public ObservableCollection<TargetControlViewModel> ManualTargetControls { get; }

    public TargetControlViewModel? PowerTargetControl
    {
        get => powerTargetControl;
        private set
        {
            if (!ReferenceEquals(powerTargetControl, value))
            {
                this.RaiseAndSetIfChanged(ref powerTargetControl, value);
                this.RaisePropertyChanged(nameof(HasPowerTargetControl));
            }
        }
    }

    [Reactive]
    public partial FtmsDiscoveredDevice? SelectedDevice { get; set; }

    [Reactive]
    public partial HeartRateDiscoveredDevice? SelectedHeartRateDevice { get; set; }

    [Reactive]
    public partial bool IsDevicePickerVisible { get; private set; }

    [Reactive]
    public partial bool IsHeartRateDevicePickerVisible { get; private set; }

    public bool IsConnected
    {
        get => isConnected;
        private set
        {
            if (isConnected == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref isConnected, value);
            this.RaisePropertyChanged(nameof(IsDisconnected));
            this.RaisePropertyChanged(nameof(IsSessionAvailable));
            this.RaisePropertyChanged(nameof(IsSessionUnavailable));
        }
    }

    public bool IsDisconnected => !IsConnected;

    [Reactive]
    public partial string? MachineName { get; private set; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string MachineChipText { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string MachineChipDetail { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string HeartRateChipText { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string HeartRateChipDetail { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string FtpChipText { get; }

    [ObservableAsProperty]
    public partial bool IsFtpSet { get; }

    [ObservableAsProperty]
    public partial bool IsWorkoutReady { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string WorkoutReadinessText { get; }

    [ObservableAsProperty]
    public partial bool IsRideInProgress { get; }

    public bool IsHeartRateConnected
    {
        get => isHeartRateConnected;
        private set
        {
            if (isHeartRateConnected == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref isHeartRateConnected, value);
            this.RaisePropertyChanged(nameof(IsHeartRateDisconnected));
            this.RaisePropertyChanged(nameof(IsSessionAvailable));
            this.RaisePropertyChanged(nameof(IsSessionUnavailable));
        }
    }

    public bool IsHeartRateDisconnected => !IsHeartRateConnected;

    public bool IsSessionAvailable => IsConnected || IsHeartRateConnected;

    public bool IsSessionUnavailable => !IsSessionAvailable;

    public bool IsControllable
    {
        get => isControllable;
        private set
        {
            if (isControllable == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref isControllable, value);
            this.RaisePropertyChanged(nameof(ControlAccessText));
            this.RaisePropertyChanged(nameof(IsControlPermissionRequired));
            this.RaisePropertyChanged(nameof(AreTargetControlsAvailable));
            this.RaisePropertyChanged(nameof(HasOpenRideControls));
        }
    }

    public bool HasControlPermission
    {
        get => hasControlPermission;
        private set
        {
            if (hasControlPermission == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref hasControlPermission, value);
            this.RaisePropertyChanged(nameof(ControlAccessText));
            this.RaisePropertyChanged(nameof(AreTargetControlsAvailable));
            this.RaisePropertyChanged(nameof(MachineStateText));
            this.RaisePropertyChanged(nameof(StartActionText));
            this.RaisePropertyChanged(nameof(IsControlPermissionRequired));
        }
    }

    public bool IsWorkoutActive
    {
        get => isWorkoutActive;
        private set
        {
            if (isWorkoutActive == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref isWorkoutActive, value);
            this.RaisePropertyChanged(nameof(MachineStateText));
            this.RaisePropertyChanged(nameof(StartActionText));
        }
    }

    public string MachineStateText => IsWorkoutActive ? "Running" : workoutSessionState switch
    {
        WorkoutSessionState.Paused => "Paused",
        WorkoutSessionState.Stopped => "Stopped",
        _ => "Ready to start",
    };

    public string ControlAccessText => !IsControllable ? "Unavailable" : HasControlPermission ? "Granted" : "Required to adjust targets";

    public bool IsControlPermissionRequired => IsControllable && !HasControlPermission;

    public string StartActionText => IsWorkoutActive ? "Session running" : workoutSessionState == WorkoutSessionState.Paused ? "Resume session" : "Start session";
    public WorkoutSessionState SessionState => workoutSessionState;

    public bool AreTargetControlsAvailable => IsControllable && TargetControls.Count > 0;

    public string PhysicalControlSynchronizationStateText => physicalControlSynchronizationState switch
    {
        PhysicalControlSynchronizationState.Listening => "Listening",
        PhysicalControlSynchronizationState.Synchronized => "Synchronized",
        _ => "Unavailable",
    };

    [Reactive]
    public partial string PhysicalControlSynchronizationStatus { get; private set; } = "Physical controls cannot be synchronized until a fitness machine is connected.";

    public bool HasPowerTargetControl => PowerTargetControl is not null;

    public bool HasManualTargetControls => ManualTargetControls.Count > 0;

    [ObservableAsProperty]
    public partial bool IsBusy { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string OperationStatus { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string StatusText { get; }

    [ObservableAsProperty]
    public partial bool CanInitiateOperation { get; }

    [ObservableAsProperty]
    public partial bool IsControlOperationExecuting { get; }

    [ObservableAsProperty]
    public partial bool IsWorking { get; }

    [Reactive]
    public partial bool IsScanning { get; private set; }

    [Reactive]
    public partial bool IsHeartRateScanning { get; private set; }

    [Reactive]
    public partial bool ShowUnavailableCapabilities { get; private set; }

    [Reactive]
    public partial bool SupportsTargetPower { get; private set; }

    [Reactive]
    public partial bool SupportsTargetInclination { get; private set; }

    [Reactive]
    public partial bool SupportsTargetResistance { get; private set; }

    [Reactive]
    public partial bool SupportsWorkoutGoals { get; private set; }

    public bool HasAdjustableTargets => SupportsTargetPower || SupportsTargetResistance || SupportsWorkoutGoals;

    [Reactive]
    public partial string TargetPowerText { get; set; } = "100";

    [Reactive]
    public partial string TargetInclinationText { get; set; } = "0.0";

    [Reactive]
    public partial string TargetResistanceText { get; set; } = "1.0";

    public TrainingViewModel Training { get; }

    public bool IsTrainingActive => Training.IsActive;

    public bool IsOpenRide => !IsTrainingActive;

    public bool HasOpenRideControls => IsOpenRide && IsControllable;

    public RideSessionMode RideSessionMode => IsTrainingActive ? RideSessionMode.StructuredTraining : RideSessionMode.OpenRide;

    public string RideTitle => IsTrainingActive ? Training.CurrentWorkoutName : "Free ride";

    public string RideSubtitle => IsTrainingActive ? "Structured workout" : "Ride freely and set targets as you go";

    public IObservable<Unit> SheetDismissRequests => sheetDismissRequests.AsObservable();

    public bool TryGetTrainingPowerCapabilities(out TrainingPowerCapabilities capabilities)
    {
        if (PowerTargetControl is { } power)
        {
            capabilities = new(
                checked((int)power.Minimum),
                checked((int)power.Maximum),
                checked((int)power.Increment));
            return capabilities.IsValid;
        }

        capabilities = default;
        return false;
    }

    public Task<bool> ApplyTrainingTargetAsync(int watts, CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(token => SetTargetPowerFromSliderAsync(watts, token), cancellationToken);

    public Task<bool> StartTrainingSessionAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(StartWorkoutSessionAsync, cancellationToken);

    public Task<bool> PauseTrainingSessionAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(PauseWorkoutSessionAsync, cancellationToken);

    public Task<bool> EndTrainingSessionAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(EndWorkoutSessionAsync, cancellationToken);

    public IObservable<DashboardDestination> NavigationRequests => navigationRequests.AsObservable();

    public void RequestNavigation(DashboardDestination destination) => navigationRequests.OnNext(destination);

    public IReadOnlyList<RecordedTelemetrySample> GetRecordedSamplesSince(DateTimeOffset timestamp) =>
        [.. recordedSamples.Where(sample => sample.CapturedAt >= timestamp)];

    [ReactiveCommand]
    private void CloseSheet() => sheetDismissRequests.OnNext(Unit.Default);

    [ReactiveCommand]
    private void ShowToday() => RequestNavigation(DashboardDestination.Today);

    [ReactiveCommand]
    private void ShowRide() => RequestNavigation(DashboardDestination.Ride);

    [ReactiveCommand]
    private void ShowTraining() => RequestNavigation(DashboardDestination.Training);

    [ReactiveCommand]
    private void ShowResults() => RequestNavigation(DashboardDestination.Results);

    [ReactiveCommand]
    private void ShowSettings() => RequestNavigation(DashboardDestination.Settings);

    [ReactiveCommand]
    private void ShowSensors() => RequestNavigation(DashboardDestination.Sensors);

    [ReactiveCommand]
    private void ShowHeartRateSensor() => RequestNavigation(DashboardDestination.HeartRateSensor);

    [ReactiveCommand]
    private void ToggleUnavailableCapabilities() => ShowUnavailableCapabilities = !ShowUnavailableCapabilities;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        DisposeCommands();
        await operationGate.WaitAsync();
        try
        {
            await StopScanAsync();
            await StopHeartRateScanAsync();
            await StopSessionAsync();
            await StopHeartRateSessionAsync();
        }
        finally
        {
            operationGate.Release();
        }

        sessionSubscriptions.Dispose();
        scanSubscriptions.Dispose();
        heartRateSessionSubscriptions.Dispose();
        heartRateScanSubscriptions.Dispose();
        subscriptions.Dispose();
        Training.Dispose();
        HeartRate.Dispose();
        _isBusyHelper.Dispose();
        _operationStatusHelper.Dispose();
        _statusTextHelper.Dispose();
        _canInitiateOperationHelper.Dispose();
        _isControlOperationExecutingHelper.Dispose();
        _isWorkingHelper.Dispose();
        telemetryIngress.OnCompleted();
        telemetrySessionReset.OnCompleted();
        sessionTerminationIngress.OnCompleted();
        heartRateIngress.OnCompleted();
        heartRateSessionReset.OnCompleted();
        heartRateSessionTerminationIngress.OnCompleted();
        navigationRequests.OnCompleted();
        sheetDismissRequests.OnCompleted();
        operationGate.Dispose();
    }

    private void DisposeCommands()
    {
        ScanCommand.Dispose();
        ConnectCommand.Dispose();
        DisconnectCommand.Dispose();
        ScanHeartRateCommand.Dispose();
        ConnectHeartRateCommand.Dispose();
        DisconnectHeartRateCommand.Dispose();
        RequestControlCommand.Dispose();
        StartCommand.Dispose();
        StopCommand.Dispose();
        StartFreeRideCommand.Dispose();
        EndFreeRideCommand.Dispose();
        CloseSheetCommand.Dispose();
        AddLapCommand.Dispose();
        ZoomToSelectionCommand.Dispose();
        ResetChartZoomCommand.Dispose();
        SetTargetInclinationCommand.Dispose();
        SetTargetPowerCommand.Dispose();
        SetTargetResistanceCommand.Dispose();
        ResetSessionCommand.Dispose();
        ExportSessionCommand.Dispose();
        LoadPreviousSessionCommand.Dispose();
        ApplyHeartRateZonesCommand.Dispose();
        ShowTodayCommand.Dispose();
        ShowRideCommand.Dispose();
        ShowTrainingCommand.Dispose();
        ShowResultsCommand.Dispose();
        ShowSettingsCommand.Dispose();
        ShowSensorsCommand.Dispose();
        ShowHeartRateSensorCommand.Dispose();
        ToggleUnavailableCapabilitiesCommand.Dispose();
        DisposeTargetControls();
    }

    [ReactiveCommand(CanExecute = nameof(canScan))]
    private Task ScanAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(_ => ScanDevicesAsync(), cancellationToken);

    private async Task ScanDevicesAsync()
    {
        try
        {
            ConnectionStatus = "Searching for nearby FTMS fitness machines";
            await StopHeartRateScanAsync();
            await StopSessionAsync();
            await StopScanAsync();
            DiscoveredDevices.Clear();
            SelectedDevice = null;
            IsDevicePickerVisible = true;
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var discoveredDevices = new Subject<FtmsDiscoveredDevice>();
            var discoveryIngress = Subject.Synchronize(discoveredDevices);
            scanCancellation = cancellation;
            scanSubscriptions.Disposable = discoveryIngress
                .Distinct(device => device.Id)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(device => AddDiscoveredDevice(cancellation, device), HandleScanPipelineError);
            IsScanning = true;
            scanConsumer = ConsumeDiscoveredDevicesAsync(cancellation, discoveryIngress);
        }
        catch (Exception)
        {
            IsDevicePickerVisible = !IsConnected;
            ConnectionStatus = "Bluetooth scanning failed. Check that Bluetooth is enabled, then scan again.";
        }
    }

    [ReactiveCommand(CanExecute = nameof(canScan))]
    private Task ScanHeartRateAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(_ => ScanHeartRateDevicesAsync(), cancellationToken);

    private async Task ScanHeartRateDevicesAsync()
    {
        try
        {
            HeartRateConnectionStatus = "Searching for nearby heart-rate sensors";
            await StopScanAsync();
            await StopHeartRateSessionAsync();
            await StopHeartRateScanAsync();
            HeartRateDiscoveredDevices.Clear();
            SelectedHeartRateDevice = null;
            IsHeartRateDevicePickerVisible = true;
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var discoveredDevices = new Subject<HeartRateDiscoveredDevice>();
            var discoveryIngress = Subject.Synchronize(discoveredDevices);
            heartRateScanCancellation = cancellation;
            heartRateScanSubscriptions.Disposable = discoveryIngress
                .Distinct(device => device.Id)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(device => AddDiscoveredHeartRateDevice(cancellation, device), HandleHeartRateScanPipelineError);
            IsHeartRateScanning = true;
            heartRateScanConsumer = ConsumeHeartRateDiscoveredDevicesAsync(cancellation, discoveryIngress);
        }
        catch (Exception)
        {
            IsHeartRateDevicePickerVisible = !IsHeartRateConnected;
            HeartRateConnectionStatus = "Heart-rate sensor scanning failed. Check Bluetooth and try again.";
        }
    }

    private async Task ExecuteExclusiveAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            await operation(cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task<TResult> ExecuteExclusiveAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    [ReactiveCommand(CanExecute = nameof(canConnect))]
    private Task ConnectAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(ConnectDeviceAsync, cancellationToken);

    private async Task ConnectDeviceAsync(CancellationToken cancellationToken)
    {
        if (SelectedDevice is null)
        {
            ConnectionStatus = "Select a fitness machine before connecting.";
            return;
        }

        try
        {
            await StopScanAsync();
            await StopHeartRateScanAsync();
            ConnectionStatus = $"Connecting to {SelectedDevice.Name}";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            session = await discoveryService.ConnectAsync(SelectedDevice, timeout.Token);
            IsDevicePickerVisible = session is null;
            IsConnected = session is not null;
            MachineName = session?.DeviceName;
            IsControllable = session?.CanControl == true;
            HasControlPermission = false;
            SetWorkoutSessionState(WorkoutSessionState.NotStarted);
            IsWorkoutActive = false;
            UpdateCapabilities(session?.Features, session?.AdvertisedMachineTypes);
            UpdateTargetControls(session?.Features, session?.SupportedRanges);
            SetPhysicalControlSynchronizationState(session);
            ControlStatus = session?.CanControl == true
                ? "Digital controls request permission automatically when you start, pause, or change a target."
                : session?.ControlPointAvailability ?? "This device does not expose a usable FTMS Control Point. Telemetry remains available.";
            ConnectionStatus = session is null
                ? $"{SelectedDevice.Name} does not expose the FTMS service. Select another device."
                : session.CanControl
                    ? $"Connected to {session.DeviceName} ({session.MachineType})."
                    : $"Connected to {session.DeviceName} ({session.MachineType}). Waiting for telemetry.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            IsDevicePickerVisible = true;
            ConnectionStatus = $"Connection to {SelectedDevice.Name} was cancelled.";
        }
        catch (OperationCanceledException)
        {
            IsDevicePickerVisible = true;
            ConnectionStatus = $"Connection to {SelectedDevice.Name} timed out. Select it and try again.";
        }
        catch (Exception)
        {
            IsDevicePickerVisible = true;
            ConnectionStatus = $"Could not connect to {SelectedDevice.Name}. Select it and try again.";
        }

        if (session is not null)
        {
            SubscribeToSessionEvents(session);
            if (session.LatestMachineStatus is { } latestMachineStatus)
            {
                HandleMachineStatusChanged(session, latestMachineStatus);
            }

            telemetryCancellation = new CancellationTokenSource();
            telemetryConsumer = ConsumeTelemetryAsync(session, telemetryCancellation.Token);
        }
    }

    [ReactiveCommand(CanExecute = nameof(canHeartRateConnect))]
    private Task ConnectHeartRateAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(ConnectHeartRateDeviceAsync, cancellationToken);

    private async Task ConnectHeartRateDeviceAsync(CancellationToken cancellationToken)
    {
        if (SelectedHeartRateDevice is null)
        {
            HeartRateConnectionStatus = "Select a heart-rate sensor before connecting.";
            return;
        }

        try
        {
            await StopScanAsync();
            await StopHeartRateScanAsync();
            HeartRateConnectionStatus = $"Connecting to {SelectedHeartRateDevice.Name}";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            heartRateSession = await heartRateDiscoveryService.ConnectAsync(SelectedHeartRateDevice, timeout.Token);
            IsHeartRateDevicePickerVisible = heartRateSession is null;
            IsHeartRateConnected = heartRateSession is not null;
            HeartRateConnectionStatus = heartRateSession is null
                ? $"{SelectedHeartRateDevice.Name} does not expose a usable Heart Rate Service. Select another sensor."
                : $"Connected to {heartRateSession.DeviceName}. Waiting for heart-rate updates.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            IsHeartRateDevicePickerVisible = true;
            HeartRateConnectionStatus = $"Connection to {SelectedHeartRateDevice.Name} was cancelled.";
        }
        catch (OperationCanceledException)
        {
            IsHeartRateDevicePickerVisible = true;
            HeartRateConnectionStatus = $"Connection to {SelectedHeartRateDevice.Name} timed out. Select it and try again.";
        }
        catch (Exception)
        {
            IsHeartRateDevicePickerVisible = true;
            HeartRateConnectionStatus = $"Could not connect to {SelectedHeartRateDevice.Name}. Select it and try again.";
        }

        if (heartRateSession is not null)
        {
            heartRateSessionReset.OnNext(Unit.Default);
            if (workoutSessionState is WorkoutSessionState.Paused or WorkoutSessionState.Stopped)
            {
                HeartRate.PauseSession(DateTimeOffset.UtcNow);
            }

            SubscribeToHeartRateSessionEvents(heartRateSession);
            heartRateTelemetryCancellation = new CancellationTokenSource();
            heartRateTelemetryConsumer = ConsumeHeartRateTelemetryAsync(heartRateSession, heartRateTelemetryCancellation.Token);
        }
    }

    [ReactiveCommand(CanExecute = nameof(canDisconnect))]
    private Task DisconnectAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(_ => DisconnectDeviceAsync(), cancellationToken);

    private async Task DisconnectDeviceAsync()
    {
        try
        {
            await StopSessionAsync();
            ConnectionStatus = "Disconnected from fitness machine.";
            IsDevicePickerVisible = true;
        }
        catch (Exception)
        {
            ConnectionStatus = "Could not disconnect from the fitness machine. Try again.";
        }
    }

    [ReactiveCommand(CanExecute = nameof(canHeartRateDisconnect))]
    private Task DisconnectHeartRateAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(_ => DisconnectHeartRateDeviceAsync(), cancellationToken);

    private async Task DisconnectHeartRateDeviceAsync()
    {
        try
        {
            await StopHeartRateSessionAsync();
            HeartRateConnectionStatus = "Disconnected from heart-rate sensor.";
            IsHeartRateDevicePickerVisible = true;
        }
        catch (Exception)
        {
            HeartRateConnectionStatus = "Could not disconnect from the heart-rate sensor. Try again.";
        }
    }

    [ReactiveCommand(CanExecute = nameof(canResetSession))]
    private async Task ExportSessionAsync(CancellationToken cancellationToken)
    {
        var fileName = $"stationary-session-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.csv";
        var path = Path.Combine(platform.CacheDirectory, fileName);
        var archive = CreateRecordedSession();
        await RecordedSessionArchiveStorage.WriteCsvAsync(path, archive, cancellationToken);
        await RecordedSessionArchiveStorage.WriteJsonAsync(GetPreviousSessionPath(), archive, cancellationToken);
        previousSession = archive;
        UpdatePreviousSessionComparison(archive.Summary);
        ConnectionStatus = $"Session telemetry exported to {path}.";
    }

    [ReactiveCommand(CanExecute = nameof(canResetSession))]
    private void ResetSession()
    {
        Telemetry.ResetSession();
        HeartRate.ResetSession();
        lapCount = 0;
        SessionEvents = [];
        ClearRecordedSession();
        ResetChartZoom();
    }

    [ReactiveCommand]
    private void ApplyHeartRateZones()
    {
        if (HeartRate.TryApplyCyclingLactateThreshold(out var error))
        {
            HeartRateConnectionStatus = IsHeartRateConnected
                ? "Cycling LTHR profile updated. Time in zones restarted."
                : "Cycling LTHR profile saved.";
            return;
        }

        HeartRateConnectionStatus = error;
    }

    [ReactiveCommand(CanExecute = nameof(canApplyTarget))]
    private Task SetTargetPowerAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(ApplyTargetPowerTextAsync, cancellationToken);

    private async Task ApplyTargetPowerTextAsync(CancellationToken cancellationToken)
    {
        if (!short.TryParse(TargetPowerText, out var watts))
        {
            return;
        }

        await SetTargetPowerFromSliderAsync(watts, cancellationToken);
    }

    [ReactiveCommand(CanExecute = nameof(canApplyTarget))]
    private Task SetTargetInclinationAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(ApplyTargetInclinationTextAsync, cancellationToken);

    private async Task ApplyTargetInclinationTextAsync(CancellationToken cancellationToken)
    {
        if (!TryGetInclinationTenths(TargetInclinationText, out var tenths))
        {
            return;
        }

        await SetTargetInclinationFromSliderAsync(tenths / 10d, cancellationToken);
    }

    [ReactiveCommand(CanExecute = nameof(canApplyTarget))]
    private Task SetTargetResistanceAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(ApplyTargetResistanceTextAsync, cancellationToken);

    private async Task ApplyTargetResistanceTextAsync(CancellationToken cancellationToken)
    {
        if (!TryGetResistanceTenths(TargetResistanceText, out var tenths))
        {
            return;
        }

        await SetTargetResistanceFromSliderAsync(tenths / 10d, cancellationToken);
    }

    [ReactiveCommand(CanExecute = nameof(canRequestControl))]
    private Task RequestControlAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(AcquireControlAsync, cancellationToken);

    private async Task AcquireControlAsync(CancellationToken cancellationToken)
    {
        await EnsureControlPermissionAsync(cancellationToken);
    }

    private async Task StartWorkoutAsync(CancellationToken cancellationToken)
    {
        await StartWorkoutSessionAsync(cancellationToken);
    }

    [ReactiveCommand(CanExecute = nameof(canStart))]
    private Task StartAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(StartWorkoutAsync, cancellationToken);

    [ReactiveCommand(CanExecute = nameof(canStop))]
    private Task StopAsync(CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(PauseWorkoutAsync, cancellationToken);

    [ReactiveCommand(CanExecute = nameof(canStartFreeRide))]
    private async Task StartFreeRideAsync(CancellationToken cancellationToken)
    {
        RequestNavigation(DashboardDestination.Ride);
        StartLocalSessionRecording();
        if (IsControllable && !IsWorkoutActive)
        {
            await ExecuteExclusiveAsync(StartWorkoutSessionAsync, cancellationToken);
        }
    }

    [ReactiveCommand(CanExecute = nameof(canEndFreeRide))]
    private async Task EndFreeRideAsync(CancellationToken cancellationToken)
    {
        var machineSessionActive = IsControllable && workoutSessionState is WorkoutSessionState.Running or WorkoutSessionState.Paused;
        if (machineSessionActive && !await ExecuteExclusiveAsync(EndWorkoutSessionAsync, cancellationToken))
        {
            return;
        }

        if (!machineSessionActive)
        {
            StopWorkoutSession();
        }

        RequestNavigation(DashboardDestination.Results);
    }

    private async Task<bool> StartWorkoutSessionAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureControlPermissionAsync(cancellationToken))
        {
            return false;
        }

        if (await ExecuteControlAsync(FtmsControlPointOpcode.StartOrResume, Array.Empty<byte>(), "Session started", cancellationToken))
        {
            StartOrResumeWorkoutSession();
            ControlStatus = "Session running. Adjust targets as needed.";
            return true;
        }

        return false;
    }

    private async Task PauseWorkoutAsync(CancellationToken cancellationToken)
    {
        await PauseWorkoutSessionAsync(cancellationToken);
    }

    private async Task<bool> PauseWorkoutSessionAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureControlPermissionAsync(cancellationToken))
        {
            return false;
        }

        if (await ExecuteControlAsync(FtmsControlPointOpcode.StopOrPause, new byte[] { PauseControlInformation }, "Session paused", cancellationToken))
        {
            PauseWorkoutSession();
            ControlStatus = "Session paused. Target adjustments remain available.";
            return true;
        }

        return false;
    }

    private async Task<bool> EndWorkoutSessionAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureControlPermissionAsync(cancellationToken))
        {
            return false;
        }

        if (await ExecuteControlAsync(FtmsControlPointOpcode.StopOrPause, new byte[] { StopControlInformation }, "Session ended", cancellationToken))
        {
            StopWorkoutSession();
            ControlStatus = "Session ended.";
            return true;
        }

        return false;
    }

    private async Task<bool> EnsureControlPermissionAsync(CancellationToken cancellationToken)
    {
        if (HasControlPermission)
        {
            return true;
        }

        if (!await ExecuteControlAsync(FtmsControlPointOpcode.RequestControl, Array.Empty<byte>(), "Digital controls enabled.", cancellationToken))
        {
            return false;
        }

        HasControlPermission = true;
        ControlStatus = "Digital controls enabled. Start, pause, and target changes are ready.";
        return true;
    }

    private async Task<bool> ExecuteControlAsync(FtmsControlPointOpcode opcode, ReadOnlyMemory<byte> parameters, string successMessage, CancellationToken cancellationToken)
    {
        var activeSession = session;
        if (activeSession is null || !activeSession.CanControl || !IsControllable)
        {
            ConnectionStatus = "This fitness machine does not expose a usable FTMS Control Point.";
            return false;
        }

        try
        {
            ConnectionStatus = $"Sending {opcode}...";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var result = await activeSession.ExecuteControlAsync(opcode, parameters, timeout.Token);
            if (!ReferenceEquals(session, activeSession))
            {
                return false;
            }

            ConnectionStatus = result == FtmsControlPointResult.Success
                ? successMessage
                : $"{opcode} was rejected: {result}.";
            return result == FtmsControlPointResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (ReferenceEquals(session, activeSession))
            {
                ConnectionStatus = $"{opcode} was cancelled.";
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(session, activeSession))
            {
                ConnectionStatus = $"{opcode} timed out.";
            }

            return false;
        }
        catch (Exception)
        {
            if (ReferenceEquals(session, activeSession))
            {
                ConnectionStatus = $"{opcode} could not be sent. Reconnect and try again.";
            }

            return false;
        }
    }

    private void SubscribeToSessionEvents(IFtmsSession activeSession)
    {
        sessionSubscriptions.Disposable = new CompositeDisposable(
            Observable.FromEventPattern(
                    handler => activeSession.TelemetryReceived += handler,
                    handler => activeSession.TelemetryReceived -= handler)
                .Synchronize()
                .Sample(TimeSpan.FromSeconds(1), timerScheduler)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(
                    _ => ConnectionStatus = $"Receiving live telemetry from {activeSession.DeviceName} ({activeSession.MachineType}, {activeSession.TelemetryPacketsReceived} packets).",
                    HandleTelemetryPipelineError),
            Observable.FromEventPattern(
                    handler => activeSession.ConnectionLost += handler,
                    handler => activeSession.ConnectionLost -= handler)
                .Synchronize()
                .Take(1)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(
                    _ => sessionTerminationIngress.OnNext(new(activeSession, SessionTerminationReason.ConnectionLost)),
                    HandleTelemetryPipelineError),
            Observable.FromEventPattern<FtmsMachineStatusChangedEventArgs>(
                    handler => activeSession.MachineStatusChanged += handler,
                    handler => activeSession.MachineStatusChanged -= handler)
                .Synchronize()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(eventPattern => HandleMachineStatusChanged(activeSession, eventPattern.EventArgs), HandleTelemetryPipelineError));
    }

    private void SubscribeToHeartRateSessionEvents(IHeartRateSession activeSession)
    {
        heartRateSessionSubscriptions.Disposable = new CompositeDisposable(
            Observable.FromEventPattern(
                    handler => activeSession.MeasurementReceived += handler,
                    handler => activeSession.MeasurementReceived -= handler)
                .Synchronize()
                .Sample(TimeSpan.FromSeconds(1), timerScheduler)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(
                    _ => HeartRateConnectionStatus = $"Receiving live heart rate from {activeSession.DeviceName} ({activeSession.MeasurementsReceived} measurements).",
                    HandleHeartRatePipelineError),
            Observable.FromEventPattern(
                    handler => activeSession.ConnectionLost += handler,
                    handler => activeSession.ConnectionLost -= handler)
                .Synchronize()
                .Take(1)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(
                    _ => heartRateSessionTerminationIngress.OnNext(new(activeSession, SessionTerminationReason.ConnectionLost)),
                    HandleHeartRatePipelineError));
    }

    private async Task HandleSessionTerminationAsync(SessionTermination termination)
    {
        if (!ReferenceEquals(session, termination.Session))
        {
            return;
        }

        Exception? cleanupException = null;
        try
        {
            await StopSessionAsync();
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        if (session is not null)
        {
            return;
        }

        ConnectionStatus = termination.Reason == SessionTerminationReason.ConnectionLost
            ? "Connection lost. Select the fitness machine and reconnect."
            : "Telemetry processing stopped unexpectedly. Reconnect to continue.";
        IsDevicePickerVisible = true;
        ControlStatus = "Connection lost. FTMS controls are unavailable.";
        if (cleanupException is not null)
        {
            HandleTelemetryPipelineError(cleanupException);
        }
    }

    private async Task HandleHeartRateSessionTerminationAsync(HeartRateSessionTermination termination)
    {
        if (!ReferenceEquals(heartRateSession, termination.Session))
        {
            return;
        }

        Exception? cleanupException = null;
        try
        {
            await StopHeartRateSessionAsync();
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        if (heartRateSession is not null)
        {
            return;
        }

        HeartRateConnectionStatus = termination.Reason == SessionTerminationReason.ConnectionLost
            ? "Heart-rate sensor connection lost. Select it and reconnect."
            : "Heart-rate processing stopped unexpectedly. Reconnect to continue.";
        IsHeartRateDevicePickerVisible = true;
        if (cleanupException is not null)
        {
            HandleHeartRatePipelineError(cleanupException);
        }
    }

    private void HandleMachineStatusChanged(IFtmsSession activeSession, FtmsMachineStatusChangedEventArgs eventArgs)
    {
        if (!ReferenceEquals(session, activeSession))
        {
            return;
        }

        SetPhysicalControlSynchronizationState(
            PhysicalControlSynchronizationState.Synchronized,
            $"Physical controls are synchronized from {activeSession.MachineStatusPacketsReceived} valid Fitness Machine Status update(s).");
        if (eventArgs.Opcode == FtmsMachineStatusOpcode.ControlPermissionLost)
        {
            HasControlPermission = false;
            ControlStatus = "Digital control was released by the fitness machine. You can enable it again.";
            return;
        }

        switch (eventArgs.Opcode)
        {
            case FtmsMachineStatusOpcode.StartedOrResumedByUser:
                var previousState = workoutSessionState;
                StartOrResumeWorkoutSession();
                ControlStatus = previousState switch
                {
                    WorkoutSessionState.Paused => "Session resumed from bike controls.",
                    WorkoutSessionState.NotStarted or WorkoutSessionState.Stopped => "Session started from bike controls.",
                    _ => "Session running.",
                };
                break;
            case FtmsMachineStatusOpcode.StoppedOrPausedByUser when eventArgs.Parameters.Span is [StopControlInformation]:
                StopWorkoutSession();
                ControlStatus = "Session stopped from bike controls.";
                break;
            case FtmsMachineStatusOpcode.StoppedOrPausedByUser when eventArgs.Parameters.Span is [PauseControlInformation]:
                PauseWorkoutSession();
                ControlStatus = "Session paused from bike controls.";
                break;
            case FtmsMachineStatusOpcode.StoppedOrPausedByUser:
                {
                    var parameters = eventArgs.Parameters.Span;
                    ControlStatus = parameters.Length == 1
                        ? $"The bike reported an unsupported stop or pause value (0x{parameters[0]:X2}). Session state was left unchanged."
                        : "The bike reported stop or pause status without valid control information. Session state was left unchanged.";
                    break;
                }
            case FtmsMachineStatusOpcode.StoppedBySafetyKey:
                StopWorkoutSession();
                ControlStatus = "Stopped by safety key.";
                break;
            case FtmsMachineStatusOpcode.Reset:
                StopWorkoutSession();
                ControlStatus = "Session reset by the fitness machine.";
                break;
        }

        if (TryGetStatusValue(eventArgs.Opcode, eventArgs.Parameters.Span, out var value))
        {
            foreach (var target in TargetControls)
            {
                if (target.StatusOpcode == eventArgs.Opcode)
                {
                    ControlStatus = target.TrySetRawValue(value)
                        ? $"The bike reported {target.Name} changed."
                        : $"The bike reported an unsupported {target.Name} value. The displayed target was left unchanged.";
                    break;
                }
            }
        }
    }

    private void StartOrResumeWorkoutSession()
    {
        var timestamp = DateTimeOffset.UtcNow;
        switch (workoutSessionState)
        {
            case WorkoutSessionState.NotStarted:
            case WorkoutSessionState.Stopped:
                if (!isSessionRecording)
                {
                    StartNewSessionRecording(timestamp);
                }

                break;
            case WorkoutSessionState.Paused:
                if (!isSessionRecording)
                {
                    ResumeSessionRecording(timestamp);
                }

                break;
            case WorkoutSessionState.Running:
                if (!isSessionRecording)
                {
                    ResumeSessionRecording(timestamp);
                }

                return;
        }

        SetWorkoutSessionState(WorkoutSessionState.Running);
        IsWorkoutActive = true;
    }

    private void PauseWorkoutSession()
    {
        if (workoutSessionState != WorkoutSessionState.Running)
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow;
        PauseSessionRecording(timestamp);
        AddSessionEvent(timestamp, SessionEventType.Pause, "Pause");
        SetWorkoutSessionState(WorkoutSessionState.Paused);
        IsWorkoutActive = false;
    }

    private void StopWorkoutSession()
    {
        var timestamp = DateTimeOffset.UtcNow;
        if (workoutSessionState is WorkoutSessionState.NotStarted or WorkoutSessionState.Stopped)
        {
            if (isSessionRecording)
            {
                PauseSessionRecording(timestamp);
                AddSessionEvent(timestamp, SessionEventType.Stop, "Stop");
            }

            return;
        }

        PauseSessionRecording(timestamp);
        AddSessionEvent(timestamp, SessionEventType.Stop, "Stop");
        SetWorkoutSessionState(WorkoutSessionState.Stopped);
        IsWorkoutActive = false;
    }

    private void StartLocalSessionRecording()
    {
        if (isSessionRecording)
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow;
        if (workoutSessionState == WorkoutSessionState.Paused)
        {
            ResumeSessionRecording(timestamp);
        }
        else
        {
            StartNewSessionRecording(timestamp);
        }
    }

    private void StartNewSessionRecording(DateTimeOffset timestamp)
    {
        Telemetry.StartSession(timestamp);
        HeartRate.StartSession();
        lapCount = 0;
        SessionEvents = [];
        ClearRecordedSession();
        isSessionRecording = true;
        AddSessionEvent(timestamp, SessionEventType.Start, "Start");
    }

    private void ResumeSessionRecording(DateTimeOffset timestamp)
    {
        Telemetry.ResumeSession(timestamp);
        HeartRate.ResumeSession();
        isSessionRecording = true;
        AddSessionEvent(timestamp, SessionEventType.Resume, "Resume");
    }

    private void PauseSessionRecording(DateTimeOffset timestamp)
    {
        if (!isSessionRecording)
        {
            return;
        }

        Telemetry.PauseSession(timestamp);
        HeartRate.PauseSession(timestamp);
        isSessionRecording = false;
    }

    [ReactiveCommand(CanExecute = nameof(canAddLap))]
    private void AddLap()
    {
        lapCount++;
        AddSessionEvent(DateTimeOffset.UtcNow, SessionEventType.Lap, $"Lap {lapCount}");
    }

    private void AddSessionEvent(DateTimeOffset timestamp, SessionEventType type, string label) =>
        SessionEvents = [.. SessionEvents, new(timestamp, type, label)];

    [ReactiveCommand]
    private void ZoomToSelection()
    {
        if (!TryGetSelectedRange(out var start, out var end) || end - start < TimeSpan.FromSeconds(1))
        {
            return;
        }

        isCustomChartRange = true;
        ChartTimeRangeStart = start;
        ChartTimeRangeEnd = end;
        UpdatePreviousComparisonSamples(start, end);
    }

    [ReactiveCommand]
    private void ResetChartZoom()
    {
        isCustomChartRange = false;
        ChartSelectionStart = null;
        ChartSelectionEnd = null;
        UpdateChartTimeRange();
    }

    private void UpdateSelectedIntervalSummary()
    {
        if (!TryGetSelectedRange(out var start, out var end))
        {
            SelectedIntervalSummary = "Drag across a chart to inspect an interval.";
            return;
        }

        var power = GetIntervalAverage(Telemetry.PowerChartSamples, start, end);
        var speed = GetIntervalAverage(Telemetry.SpeedChartSamples, start, end);
        var cadence = GetIntervalAverage(Telemetry.CadenceChartSamples, start, end);
        var heartRate = GetIntervalAverage(HeartRate.ChartSamples, start, end);
        SelectedIntervalSummary = $"{(end - start).ToString(@"mm\:ss", CultureInfo.InvariantCulture)} selected · {FormatIntervalValue(power, "W")} · {FormatIntervalValue(speed, "km/h")} · {FormatIntervalValue(cadence, "rpm")} · {FormatIntervalValue(heartRate, "bpm")}";
    }

    private bool TryGetSelectedRange(out DateTimeOffset start, out DateTimeOffset end)
    {
        start = default;
        end = default;
        if (ChartSelectionStart is not { } first || ChartSelectionEnd is not { } second || first == second)
        {
            return false;
        }

        start = first < second ? first : second;
        end = first < second ? second : first;
        return true;
    }

    private static double? GetIntervalAverage(IReadOnlyList<TelemetrySample> samples, DateTimeOffset start, DateTimeOffset end)
    {
        var values = samples.Where(sample => sample.CapturedAt >= start && sample.CapturedAt <= end).Select(sample => sample.Value).ToArray();
        return values.Length > 0 ? values.Average() : null;
    }

    private static string FormatIntervalValue(double? value, string unit) => value is double measurement
        ? $"{measurement:F0} {unit}"
        : $"-- {unit}";

    private void SetWorkoutSessionState(WorkoutSessionState value)
    {
        if (workoutSessionState == value)
        {
            return;
        }

        workoutSessionState = value;
        this.RaisePropertyChanged(nameof(SessionState));
        this.RaisePropertyChanged(nameof(MachineStateText));
        this.RaisePropertyChanged(nameof(StartActionText));
    }

    private void SetPhysicalControlSynchronizationState(IFtmsSession? activeSession)
    {
        if (activeSession is null)
        {
            SetPhysicalControlSynchronizationState(
                PhysicalControlSynchronizationState.Unavailable,
                "Physical controls cannot be synchronized until a fitness machine is connected.");
            return;
        }

        var state = activeSession.IsMachineStatusSubscribed
            ? activeSession.MachineStatusPacketsReceived > 0
                ? PhysicalControlSynchronizationState.Synchronized
                : PhysicalControlSynchronizationState.Listening
            : PhysicalControlSynchronizationState.Unavailable;
        SetPhysicalControlSynchronizationState(state, activeSession.MachineStatusAvailability);
    }

    private void SetPhysicalControlSynchronizationState(PhysicalControlSynchronizationState state, string status)
    {
        if (physicalControlSynchronizationState != state)
        {
            physicalControlSynchronizationState = state;
            this.RaisePropertyChanged(nameof(PhysicalControlSynchronizationStateText));
        }

        PhysicalControlSynchronizationStatus = status;
    }

    private async Task ConsumeTelemetryAsync(IFtmsSession activeSession, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var snapshot in activeSession.Telemetry.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                telemetryIngress.OnNext(new(activeSession, snapshot));
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                sessionTerminationIngress.OnNext(new(activeSession, SessionTerminationReason.TelemetryFailure));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            sessionTerminationIngress.OnNext(new(activeSession, SessionTerminationReason.TelemetryFailure));
        }
    }

    private async Task ConsumeHeartRateTelemetryAsync(IHeartRateSession activeSession, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var observation in activeSession.Telemetry.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                heartRateIngress.OnNext(new(activeSession, observation));
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                heartRateSessionTerminationIngress.OnNext(new(activeSession, SessionTerminationReason.TelemetryFailure));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            heartRateSessionTerminationIngress.OnNext(new(activeSession, SessionTerminationReason.TelemetryFailure));
        }
    }

    private async Task ConsumeDiscoveredDevicesAsync(CancellationTokenSource cancellation, IObserver<FtmsDiscoveredDevice> discoveredDevices)
    {
        var outcome = ScanOutcome.Completed;
        try
        {
            await discoveryService.DiscoverAsync(discoveredDevices.OnNext, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            outcome = ScanOutcome.Cancelled;
        }
        catch (Exception)
        {
            outcome = ScanOutcome.Failed;
        }
        finally
        {
            await platform.InvokeOnMainThreadAsync(() => CompleteScan(cancellation, outcome)).ConfigureAwait(false);
        }
    }

    private async Task ConsumeHeartRateDiscoveredDevicesAsync(CancellationTokenSource cancellation, IObserver<HeartRateDiscoveredDevice> discoveredDevices)
    {
        var outcome = ScanOutcome.Completed;
        try
        {
            await heartRateDiscoveryService.DiscoverAsync(discoveredDevices.OnNext, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            outcome = ScanOutcome.Cancelled;
        }
        catch (Exception)
        {
            outcome = ScanOutcome.Failed;
        }
        finally
        {
            await platform.InvokeOnMainThreadAsync(() => CompleteHeartRateScan(cancellation, outcome)).ConfigureAwait(false);
        }
    }

    private void AddDiscoveredDevice(CancellationTokenSource cancellation, FtmsDiscoveredDevice device)
    {
        if (ReferenceEquals(scanCancellation, cancellation))
        {
            DiscoveredDevices.Add(device);
            ConnectionStatus = $"Found {DiscoveredDevices.Count} Bluetooth device(s). Select a fitness machine to connect.";
        }
    }

    private void AddDiscoveredHeartRateDevice(CancellationTokenSource cancellation, HeartRateDiscoveredDevice device)
    {
        if (ReferenceEquals(heartRateScanCancellation, cancellation))
        {
            HeartRateDiscoveredDevices.Add(device);
            HeartRateConnectionStatus = $"Found {HeartRateDiscoveredDevices.Count} heart-rate sensor(s). Select one to connect.";
        }
    }

    private void CompleteScan(CancellationTokenSource cancellation, ScanOutcome outcome)
    {
        if (!ReferenceEquals(scanCancellation, cancellation))
        {
            return;
        }

        IsScanning = false;
        scanSubscriptions.Disposable = Disposable.Empty;
        scanCancellation.Dispose();
        scanCancellation = null;
        scanConsumer = null;
        ConnectionStatus = outcome switch
        {
            ScanOutcome.Failed => "Bluetooth scanning failed. Check that Bluetooth is enabled, then scan again.",
            ScanOutcome.Cancelled => "Bluetooth scanning was cancelled.",
            _ when DiscoveredDevices.Count == 0 => "No Bluetooth devices found. Wake the bike and scan again.",
            _ => $"Scan finished. Found {DiscoveredDevices.Count} Bluetooth device(s).",
        };
    }

    private void CompleteHeartRateScan(CancellationTokenSource cancellation, ScanOutcome outcome)
    {
        if (!ReferenceEquals(heartRateScanCancellation, cancellation))
        {
            return;
        }

        IsHeartRateScanning = false;
        heartRateScanSubscriptions.Disposable = Disposable.Empty;
        heartRateScanCancellation.Dispose();
        heartRateScanCancellation = null;
        heartRateScanConsumer = null;
        HeartRateConnectionStatus = outcome switch
        {
            ScanOutcome.Failed => "Heart-rate sensor scanning failed. Check Bluetooth and try again.",
            ScanOutcome.Cancelled => "Heart-rate sensor scanning was cancelled.",
            _ when HeartRateDiscoveredDevices.Count == 0 => "No heart-rate sensors found. Enable broadcasting on the watch and scan again.",
            _ => $"Scan finished. Found {HeartRateDiscoveredDevices.Count} heart-rate sensor(s).",
        };
    }

    private async ValueTask StopScanAsync()
    {
        var cancellation = scanCancellation;
        var consumer = scanConsumer;
        scanCancellation = null;
        scanConsumer = null;
        scanSubscriptions.Disposable = Disposable.Empty;
        cancellation?.Cancel();
        if (consumer is not null)
        {
            await consumer;
        }

        cancellation?.Dispose();
        IsScanning = false;
    }

    private async ValueTask StopHeartRateScanAsync()
    {
        var cancellation = heartRateScanCancellation;
        var consumer = heartRateScanConsumer;
        heartRateScanCancellation = null;
        heartRateScanConsumer = null;
        heartRateScanSubscriptions.Disposable = Disposable.Empty;
        cancellation?.Cancel();
        if (consumer is not null)
        {
            await consumer;
        }

        cancellation?.Dispose();
        IsHeartRateScanning = false;
    }

    private void HandleScanPipelineError(Exception exception) => ConnectionStatus = $"Bluetooth discovery processing failed: {exception.Message}";

    private void HandleHeartRateScanPipelineError(Exception exception) => HeartRateConnectionStatus = $"Heart-rate sensor discovery processing failed: {exception.Message}";

    private void Present(TelemetrySnapshot snapshot)
    {
        latestRecordedTelemetry = snapshot;
        Telemetry.Present(snapshot);
        RecordSessionSample(snapshot.CapturedAt);
    }

    private void PresentHeartRate(HeartRateObservation observation)
    {
        latestRecordedHeartRate = observation;
        HeartRate.Present(observation);
        RecordSessionSample(observation.CapturedAt);
    }

    private void RecordSessionSample(DateTimeOffset timestamp)
    {
        if (!isSessionRecording
            || lastRecordedAt is { } previous && timestamp - previous < TimeSpan.FromSeconds(1))
        {
            return;
        }

        TelemetrySnapshot? machine = latestRecordedTelemetry is { } telemetry
            && (timestamp - telemetry.CapturedAt).Duration() <= RecordedValueFreshness
                ? telemetry
                : null;
        HeartRateObservation? heartRate = latestRecordedHeartRate is { } observation
            && (timestamp - observation.CapturedAt).Duration() <= RecordedValueFreshness
                ? observation
                : null;
        if (machine is null && heartRate is null)
        {
            return;
        }

        recordedSamples.Enqueue(new(
            timestamp,
            machine?.PowerWatts,
            machine?.SpeedKilometersPerHour,
            machine?.CadenceRpm,
            heartRate?.BeatsPerMinute));
        while (recordedSamples.Count > MaximumRecordedSamples)
        {
            recordedSamples.Dequeue();
        }

        lastRecordedAt = timestamp;
        UpdateWorkoutAnalysis(timestamp);
        if (previousSession is not null)
        {
            UpdatePreviousSessionComparison(Telemetry.GetSessionSummary());
        }
    }

    private void ClearRecordedSession()
    {
        recordedSamples.Clear();
        lastRecordedAt = null;
        lastWorkoutAnalysisAt = null;
        EfficiencyText = "Gathering paired data";
        DecouplingText = "Requires 10 minutes";
        CadenceAnalysisText = "Gathering cadence";
        TorqueExposureText = "--";
        RecoveryText = "Waiting for recovery";
    }

    private void UpdateWorkoutAnalysis(DateTimeOffset timestamp)
    {
        if (lastWorkoutAnalysisAt is { } previous && timestamp - previous < TimeSpan.FromSeconds(5))
        {
            return;
        }

        lastWorkoutAnalysisAt = timestamp;
        var cutoff = timestamp - TimeSpan.FromMinutes(20);
        var metrics = WorkoutAnalysis.Calculate([.. recordedSamples.Where(sample => sample.CapturedAt >= cutoff)]);
        EfficiencyText = metrics.PowerHeartRateEfficiency is double efficiency ? $"{efficiency:F2} W/bpm" : "Gathering paired data";
        DecouplingText = metrics.AerobicDecouplingPercentage is double decoupling ? $"{decoupling:+0.0;-0.0;0.0}%" : "Requires 10 minutes";
        CadenceAnalysisText = metrics.AverageCadenceRpm is double average
            ? $"{average:F0} rpm · {metrics.CadenceVariationPercentage:F1}% variation · preferred {metrics.PreferredCadenceMinimumRpm:F0}-{metrics.PreferredCadenceMaximumRpm:F0}"
            : "Gathering cadence";
        TorqueExposureText = metrics.LowCadenceHighTorqueExposure.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        RecoveryText = metrics.HeartRateRecoveryBeatsPerMinute is double recovery ? $"{recovery:F0} bpm/min" : "Waiting for recovery";
    }

    private RecordedSession CreateRecordedSession() => new(
        SessionArchiveFormatVersion,
        DateTimeOffset.UtcNow,
        [.. recordedSamples],
        [.. SessionEvents],
        Telemetry.GetSessionSummary());

    [ReactiveCommand(AccessModifier = PropertyAccessModifier.Private)]
    private async Task<PreviousSessionLoadResult> LoadPreviousSessionAsync(CancellationToken cancellationToken)
    {
        var path = GetPreviousSessionPath();
        try
        {
            return new(await RecordedSessionArchiveStorage.ReadJsonAsync(path, cancellationToken).ConfigureAwait(false), null);
        }
        catch (JsonException)
        {
            return new(null, "Previous session data could not be read.");
        }
        catch (FileNotFoundException)
        {
            return default;
        }
        catch (DirectoryNotFoundException)
        {
            return default;
        }
        catch (IOException)
        {
            return new(null, "Previous session data is unavailable.");
        }
    }

    private void ApplyPreviousSessionLoadResult(PreviousSessionLoadResult result)
    {
        if (result.ErrorMessage is { } errorMessage)
        {
            PreviousSessionComparisonText = errorMessage;
            return;
        }

        if (result.Session is not { } loadedSession)
        {
            return;
        }

        previousSession = loadedSession;
        var elapsed = loadedSession.Summary.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        PreviousSessionComparisonText = $"Previous: {loadedSession.Summary.AveragePowerWatts:F0} W average · {loadedSession.Summary.CyclingMetrics.TotalWorkKilojoules:F1} kJ · {elapsed}";
    }

    private readonly record struct PreviousSessionLoadResult(RecordedSession? Session, string? ErrorMessage);

    private void UpdatePreviousSessionComparison(SessionSummary current)
    {
        if (previousSession is not { } previous)
        {
            PreviousSessionComparisonText = "No previous session saved.";
            return;
        }

        var powerDelta = current.AveragePowerWatts - previous.Summary.AveragePowerWatts;
        var workDelta = current.CyclingMetrics.TotalWorkKilojoules - previous.Summary.CyclingMetrics.TotalWorkKilojoules;
        PreviousSessionComparisonText = $"Vs previous: power {powerDelta:+0;-0;0} W · work {workDelta:+0.0;-0.0;0.0} kJ";
    }

    private string GetPreviousSessionPath() => Path.Combine(platform.AppDataDirectory, "previous-session.json");

    private void ClearTelemetryPresentation() => Telemetry.Clear();

    private IObservable<IFtmsSession> ObserveTelemetrySilence(IFtmsSession activeSession)
    {
        var silenceTimer = Observable.Timer(TimeSpan.FromSeconds(5), timerScheduler)
            .Select(_ => activeSession);
        return telemetryIngress
            .Where(telemetry => ReferenceEquals(telemetry.Session, activeSession))
            .Select(_ => silenceTimer)
            .StartWith(silenceTimer)
            .Switch();
    }

    private IObservable<IHeartRateSession> ObserveHeartRateSilence(IHeartRateSession activeSession)
    {
        var silenceTimer = Observable.Timer(TimeSpan.FromSeconds(5), timerScheduler)
            .Select(_ => activeSession);
        return heartRateIngress
            .Where(telemetry => ReferenceEquals(telemetry.Session, activeSession))
            .Select(_ => silenceTimer)
            .StartWith(silenceTimer)
            .Switch();
    }

    private async ValueTask StopSessionAsync()
    {
        var cancellation = telemetryCancellation;
        var consumer = telemetryConsumer;
        var activeSession = session;
        if (activeSession is not null && workoutSessionState is not WorkoutSessionState.NotStarted)
        {
            AddSessionEvent(DateTimeOffset.UtcNow, SessionEventType.Disconnect, "Fitness machine disconnected");
        }

        telemetryCancellation = null;
        telemetryConsumer = null;
        session = null;
        latestRecordedTelemetry = null;
        IsConnected = false;
        MachineName = null;
        sessionSubscriptions.Disposable = Disposable.Empty;
        cancellation?.Cancel();
        IsControllable = false;
        HasControlPermission = false;
        SetWorkoutSessionState(WorkoutSessionState.NotStarted);
        IsWorkoutActive = false;
        ControlStatus = "Connect a fitness machine to inspect FTMS controls.";
        SetPhysicalControlSynchronizationState(null);
        UpdateCapabilities(null, null);
        DisposeTargetControls();
        try
        {
            if (consumer is not null)
            {
                try
                {
                    await consumer;
                }
                catch (OperationCanceledException)
                {
                }
            }

        }
        finally
        {
            try
            {
                if (activeSession is not null)
                {
                    await activeSession.DisposeAsync();
                }
            }
            finally
            {
                cancellation?.Dispose();
            }
        }
    }

    private async ValueTask StopHeartRateSessionAsync()
    {
        latestRecordedHeartRate = null;
        var cancellation = heartRateTelemetryCancellation;
        var consumer = heartRateTelemetryConsumer;
        var activeSession = heartRateSession;
        heartRateTelemetryCancellation = null;
        heartRateTelemetryConsumer = null;
        heartRateSession = null;
        IsHeartRateConnected = false;
        heartRateSessionSubscriptions.Disposable = Disposable.Empty;
        cancellation?.Cancel();
        heartRateSessionReset.OnNext(Unit.Default);
        try
        {
            if (consumer is not null)
            {
                try
                {
                    await consumer;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        finally
        {
            try
            {
                if (activeSession is not null)
                {
                    await activeSession.DisposeAsync();
                }
            }
            finally
            {
                cancellation?.Dispose();
            }
        }
    }

    private void UpdateCapabilities(FitnessMachineFeature? features, IReadOnlyList<FtmsMachineDataType>? advertisedMachineTypes)
    {
        DeviceCapabilities.Clear();
        LiveCapabilities.Clear();
        AvailableControlCapabilities.Clear();
        UnavailableCapabilities.Clear();
        telemetrySessionReset.OnNext(Unit.Default);
        SupportsTargetInclination = features?.SupportsTargetInclination == true;
        SupportsTargetPower = features?.SupportsTargetPower == true;
        SupportsTargetResistance = features?.SupportsTargetResistanceLevel == true;
        SupportsWorkoutGoals = features is
        {
            SupportsTargetedExpendedEnergy: true
        } or
        {
            SupportsTargetedStepNumber: true
        } or
        {
            SupportsTargetedStrideNumber: true
        } or
        {
            SupportsTargetedDistance: true
        } or
        {
            SupportsTargetedTrainingTime: true
        };
        this.RaisePropertyChanged(nameof(HasAdjustableTargets));
        if (features is not { } value)
        {
            DeviceCapabilities.Add(new("Feature characteristic unavailable", false));
            RefreshCapabilityGroups();
            return;
        }

        if (advertisedMachineTypes is { Count: > 0 })
        {
            DeviceCapabilities.Add(new($"Advertised machine types: {string.Join(", ", advertisedMachineTypes)}", false));
        }

        AddTelemetryCapability(value.SupportsAverageSpeed, "Average speed");
        AddTelemetryCapability(value.SupportsCadence, "Cadence");
        AddTelemetryCapability(value.SupportsTotalDistance, "Total distance");
        AddTelemetryCapability(value.SupportsInclination, "Inclination");
        AddTelemetryCapability(value.SupportsElevationGain, "Elevation gain");
        AddTelemetryCapability(value.SupportsPace, "Pace");
        AddTelemetryCapability(value.SupportsStepCount, "Step count");
        AddTelemetryCapability(value.SupportsResistanceLevel, "Resistance level");
        AddTelemetryCapability(value.SupportsStrideCount, "Stride count");
        AddTelemetryCapability(value.SupportsExpendedEnergy, "Total energy");
        AddTelemetryCapability(value.SupportsExpendedEnergy, "Energy per hour");
        AddTelemetryCapability(value.SupportsExpendedEnergy, "Energy per minute");
        AddTelemetryCapability(value.SupportsHeartRateMeasurement, "Heart rate");
        AddTelemetryCapability(value.SupportsMetabolicEquivalent, "Metabolic equivalent");
        AddTelemetryCapability(value.SupportsElapsedTime, "Elapsed time");
        AddTelemetryCapability(value.SupportsRemainingTime, "Remaining time");
        AddTelemetryCapability(value.SupportsPowerMeasurement, "Power measurement");
        AddTelemetryCapability(value.SupportsForceOnBelt, "Force on belt");
        AddCapability(value.SupportsUserDataRetention, "User data retention");
        AddCapability(value.SupportsTargetSpeed, "Set target speed");
        AddCapability(value.SupportsTargetInclination, "Set target inclination");
        AddCapability(value.SupportsTargetResistanceLevel, "Set target resistance");
        AddCapability(value.SupportsTargetPower, "Set target power");
        AddCapability(value.SupportsTargetHeartRate, "Set target heart rate");
        AddCapability(value.SupportsTargetedExpendedEnergy, "Set target energy");
        AddCapability(value.SupportsTargetedStepNumber, "Set target steps");
        AddCapability(value.SupportsTargetedStrideNumber, "Set target strides");
        AddCapability(value.SupportsTargetedDistance, "Set target distance");
        AddCapability(value.SupportsTargetedTrainingTime, "Set target time");
        AddCapability(value.SupportsTargetedTimeInTwoHeartRateZones, "Set two heart-rate zones");
        AddCapability(value.SupportsTargetedTimeInThreeHeartRateZones, "Set three heart-rate zones");
        AddCapability(value.SupportsTargetedTimeInFiveHeartRateZones, "Set five heart-rate zones");
        AddCapability(value.SupportsIndoorBikeSimulationParameters, "Indoor bike simulation");
        AddCapability(value.SupportsWheelCircumference, "Set wheel circumference");
        AddCapability(value.SupportsSpinDownControl, "Spin down");
        AddCapability(value.SupportsTargetedCadence, "Set target cadence");
        RefreshCapabilityGroups();
    }

    private void AddCapability(bool supported, string name)
    {
        if (supported)
        {
            DeviceCapabilities.Add(new(name, false));
        }
    }

    private void AddTelemetryCapability(bool supported, string name)
    {
        if (supported)
        {
            DeviceCapabilities.Add(new(name, true));
        }
    }

    private void ApplyTelemetryFieldUpdate(TelemetryFieldUpdate update)
    {
        if (DeviceCapabilities.FirstOrDefault(capability => capability.Name == update.Name) is { } capability)
        {
            capability.IsObserved = update.IsObserved;
            capability.IsUnavailable = update.IsUnavailable;
            capability.IsStale = update.IsStalled;
            RefreshCapabilityGroups();
        }
    }

    private void RefreshCapabilityGroups()
    {
        LiveCapabilities.Clear();
        AvailableControlCapabilities.Clear();
        UnavailableCapabilities.Clear();
        foreach (var capability in DeviceCapabilities)
        {
            if (!capability.IsTelemetry)
            {
                AvailableControlCapabilities.Add(capability);
            }
            else if (capability.IsNotProvided)
            {
                UnavailableCapabilities.Add(capability);
            }
            else
            {
                LiveCapabilities.Add(capability);
            }
        }
    }

    private void HandleTelemetryPipelineError(Exception exception) => ConnectionStatus = $"Telemetry processing failed: {exception.Message}";

    private void HandleHeartRatePipelineError(Exception exception) => HeartRateConnectionStatus = $"Heart-rate processing failed: {exception.Message}";

    private void UpdateTargetControls(FitnessMachineFeature? features, FtmsSupportedRanges? supportedRanges)
    {
        DisposeTargetControls();
        if (features is not { } feature)
        {
            return;
        }

        var ranges = supportedRanges;
        if (feature.SupportsTargetSpeed && ranges?.Speed is FtmsUInt16Range speed)
        {
            AddTargetControl("Speed", "km/h", speed.Minimum / 100d, speed.Maximum / 100d, speed.Increment / 100d, FtmsMachineStatusOpcode.TargetSpeedChanged, raw => raw / 100d, SetTargetSpeedAsync);
        }

        if (feature.SupportsTargetInclination && ranges?.Inclination is FtmsInt16Range inclination)
        {
            AddTargetControl("Incline", "%", inclination.Minimum / 10d, inclination.Maximum / 10d, inclination.Increment / 10d, FtmsMachineStatusOpcode.TargetInclineChanged, raw => raw / 10d, SetTargetInclinationFromSliderAsync);
        }

        if (feature.SupportsTargetResistanceLevel && ranges?.Resistance is FtmsInt16Range resistance && resistance.Minimum >= byte.MinValue && resistance.Maximum <= byte.MaxValue)
        {
            AddTargetControl("Resistance", "", resistance.Minimum / 10d, resistance.Maximum / 10d, resistance.Increment / 10d, FtmsMachineStatusOpcode.TargetResistanceLevelChanged, raw => raw / 10d, SetTargetResistanceFromSliderAsync);
        }

        if (feature.SupportsTargetPower && ranges?.Power is FtmsUInt16Range power && power.Maximum <= short.MaxValue)
        {
            AddTargetControl("Power", "W", power.Minimum, power.Maximum, power.Increment, FtmsMachineStatusOpcode.TargetPowerChanged, raw => raw, SetTargetPowerFromSliderAsync, autoApplyOnValueChange: true);
        }

        if (feature.SupportsTargetHeartRate && ranges?.HeartRate is FtmsByteRange heartRate)
        {
            AddTargetControl("Heart rate", "bpm", heartRate.Minimum, heartRate.Maximum, heartRate.Increment, FtmsMachineStatusOpcode.TargetHeartRateChanged, raw => raw, SetTargetHeartRateAsync);
        }

        AddGoalControl(feature.SupportsTargetedExpendedEnergy, "Energy goal", "kcal", FtmsControlPointOpcode.SetTargetedExpendedEnergy, FtmsMachineStatusOpcode.TargetedExpendedEnergyChanged, raw => raw, 1, 9_999, SetTargetUInt16Async);
        AddGoalControl(feature.SupportsTargetedStepNumber, "Step goal", "steps", FtmsControlPointOpcode.SetTargetedSteps, FtmsMachineStatusOpcode.TargetedStepsChanged, raw => raw, 1, 65_535, SetTargetUInt16Async);
        AddGoalControl(feature.SupportsTargetedStrideNumber, "Stride goal", "strides", FtmsControlPointOpcode.SetTargetedStrides, FtmsMachineStatusOpcode.TargetedStridesChanged, raw => raw, 1, 65_535, SetTargetUInt16Async);
        AddGoalControl(feature.SupportsTargetedDistance, "Distance goal", "m", FtmsControlPointOpcode.SetTargetedDistance, FtmsMachineStatusOpcode.TargetedDistanceChanged, raw => raw, 1, 16_777_215, SetTargetUInt24Async);
        AddGoalControl(feature.SupportsTargetedTrainingTime, "Time goal", "min", FtmsControlPointOpcode.SetTargetedTrainingTime, FtmsMachineStatusOpcode.TargetedTrainingTimeChanged, raw => raw / 60d, 1, 1_440, SetTargetMinutesAsync);
    }

    private void DisposeTargetControls()
    {
        foreach (var control in TargetControls)
        {
            control.Dispose();
        }

        TargetControls.Clear();
        ManualTargetControls.Clear();
        PowerTargetControl = null;
        this.RaisePropertyChanged(nameof(AreTargetControlsAvailable));
        this.RaisePropertyChanged(nameof(HasManualTargetControls));
    }

    private void AddGoalControl(bool supported, string name, string unit, FtmsControlPointOpcode opcode, FtmsMachineStatusOpcode statusOpcode, Func<int, double> fromRawValue, double minimum, double maximum, Func<FtmsControlPointOpcode, double, CancellationToken, Task<bool>> applyAsync)
    {
        if (supported)
        {
            AddTargetControl(name, unit, minimum, maximum, 1, statusOpcode, fromRawValue, (value, cancellationToken) => applyAsync(opcode, value, cancellationToken));
        }
    }

    private static bool TryGetStatusValue(FtmsMachineStatusOpcode opcode, ReadOnlySpan<byte> parameters, out int value)
    {
        value = 0;
        switch (parameters.Length)
        {
            case 1:
                value = parameters[0];
                return true;
            case 2:
                value = opcode is FtmsMachineStatusOpcode.TargetInclineChanged or FtmsMachineStatusOpcode.TargetResistanceLevelChanged or FtmsMachineStatusOpcode.TargetPowerChanged
                    ? BinaryPrimitives.ReadInt16LittleEndian(parameters)
                    : BinaryPrimitives.ReadUInt16LittleEndian(parameters);
                return true;
            case 3:
                value = parameters[0] | (parameters[1] << 8) | (parameters[2] << 16);
                return true;
            default:
                return false;
        }
    }

    private void AddTargetControl(string name, string unit, double minimum, double maximum, double increment, FtmsMachineStatusOpcode statusOpcode, Func<int, double> fromRawValue, Func<double, CancellationToken, Task<bool>> applyAsync, bool autoApplyOnValueChange = false)
    {
        if (increment > 0 && maximum >= minimum)
        {
            IObservable<bool> canApply = autoApplyOnValueChange
                ? this.WhenAnyValue(viewModel => viewModel.IsControllable)
                : this.WhenAnyValue(viewModel => viewModel.IsControllable, viewModel => viewModel.CanInitiateOperation, (controllable, canInitiate) => controllable && canInitiate);
            canApply = canApply
                .ObserveOn(RxApp.MainThreadScheduler);
            var targetControl = new TargetControlViewModel(
                name,
                unit,
                minimum,
                maximum,
                increment,
                statusOpcode,
                fromRawValue,
                (value, cancellationToken) => ExecuteExclusiveAsync(token => applyAsync(value, token), cancellationToken),
                canApply,
                autoApplyOnValueChange,
                timerScheduler);
            TargetControls.Add(targetControl);
            if (autoApplyOnValueChange)
            {
                PowerTargetControl = targetControl;
            }
            else
            {
                ManualTargetControls.Add(targetControl);
                this.RaisePropertyChanged(nameof(HasManualTargetControls));
            }

            this.RaisePropertyChanged(nameof(AreTargetControlsAvailable));
        }
    }

    private Task<bool> SetTargetSpeedAsync(double value, CancellationToken cancellationToken) => SetTargetUInt16Async(FtmsControlPointOpcode.SetTargetSpeed, checked((ushort)double.Round(value * 100d)), $"Target speed set to {value:F2} km/h", cancellationToken);

    private Task<bool> SetTargetInclinationFromSliderAsync(double value, CancellationToken cancellationToken) => SetTargetInt16Async(FtmsControlPointOpcode.SetTargetInclination, checked((short)double.Round(value * 10d)), $"Target incline set to {value:F1}%", cancellationToken);

    private Task<bool> SetTargetPowerFromSliderAsync(double value, CancellationToken cancellationToken) => SetTargetInt16Async(FtmsControlPointOpcode.SetTargetPower, checked((short)double.Round(value)), $"Target power set to {value:F0} W", cancellationToken);

    private Task<bool> SetTargetResistanceFromSliderAsync(double value, CancellationToken cancellationToken) => ExecuteTargetControlAsync(FtmsControlPointOpcode.SetTargetResistanceLevel, new byte[] { checked((byte)double.Round(value * 10d)) }, $"Target resistance set to {value:F1}", cancellationToken);

    private Task<bool> SetTargetHeartRateAsync(double value, CancellationToken cancellationToken) => ExecuteTargetControlAsync(FtmsControlPointOpcode.SetTargetHeartRate, new byte[] { checked((byte)double.Round(value)) }, $"Target heart rate set to {value:F0} bpm", cancellationToken);

    private Task<bool> SetTargetUInt16Async(FtmsControlPointOpcode opcode, double value, CancellationToken cancellationToken) => SetTargetUInt16Async(opcode, checked((ushort)double.Round(value)), $"{opcode} set to {value:F0}.", cancellationToken);

    private Task<bool> SetTargetUInt16Async(FtmsControlPointOpcode opcode, ushort value, string successMessage, CancellationToken cancellationToken)
    {
        var parameters = new byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(parameters, value);
        return ExecuteTargetControlAsync(opcode, parameters, successMessage, cancellationToken);
    }

    private Task<bool> SetTargetUInt24Async(FtmsControlPointOpcode opcode, double value, CancellationToken cancellationToken)
    {
        var rawValue = checked((int)double.Round(value));
        var parameters = new byte[3];
        parameters[0] = (byte)rawValue;
        parameters[1] = (byte)(rawValue >> 8);
        parameters[2] = (byte)(rawValue >> 16);
        return ExecuteTargetControlAsync(opcode, parameters, $"{opcode} set to {value:F0}.", cancellationToken);
    }

    private Task<bool> SetTargetMinutesAsync(FtmsControlPointOpcode opcode, double value, CancellationToken cancellationToken) => SetTargetUInt16Async(opcode, checked((ushort)(double.Round(value) * 60d)), $"Training time set to {value:F0} min.", cancellationToken);

    private Task<bool> SetTargetInt16Async(FtmsControlPointOpcode opcode, short value, string successMessage, CancellationToken cancellationToken)
    {
        var parameters = new byte[sizeof(short)];
        BinaryPrimitives.WriteInt16LittleEndian(parameters, value);
        return ExecuteTargetControlAsync(opcode, parameters, successMessage, cancellationToken);
    }

    private async Task<bool> ExecuteTargetControlAsync(FtmsControlPointOpcode opcode, ReadOnlyMemory<byte> parameters, string successMessage, CancellationToken cancellationToken) =>
        await EnsureControlPermissionAsync(cancellationToken)
            ? await ExecuteControlAsync(opcode, parameters, successMessage, cancellationToken)
            : false;

    private static bool TryGetInclinationTenths(string value, out short tenths) =>
        TryGetTenths(value, short.MinValue, short.MaxValue, out tenths);

    private static bool TryGetResistanceTenths(string value, out byte tenths)
    {
        if (!TryGetTenths(value, byte.MinValue, byte.MaxValue, out var parsed))
        {
            tenths = 0;
            return false;
        }

        tenths = (byte)parsed;
        return true;
    }

    private static bool TryGetTenths(string value, short minimum, short maximum, out short tenths)
    {
        tenths = 0;
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var resistance))
        {
            return false;
        }

        var scaled = decimal.Round(resistance * 10m, MidpointRounding.AwayFromZero);
        if (scaled < minimum || scaled > maximum)
        {
            return false;
        }

        tenths = decimal.ToInt16(scaled);
        return true;
    }

    private sealed class TelemetryChartHistory
    {
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
        private readonly TelemetryHistory power = new(Window);
        private readonly TelemetryHistory speed = new(Window);
        private readonly TelemetryHistory cadence = new(Window);

        public TelemetryChartHistory Add(TelemetrySnapshot snapshot)
        {
            power.Trim(snapshot.CapturedAt);
            speed.Trim(snapshot.CapturedAt);
            cadence.Trim(snapshot.CapturedAt);
            Add(power, snapshot.CapturedAt, snapshot.PowerWatts);
            Add(speed, snapshot.CapturedAt, snapshot.SpeedKilometersPerHour);
            Add(cadence, snapshot.CapturedAt, snapshot.CadenceRpm);
            return this;
        }

        public TelemetryChartWindow Snapshot() => new([.. power.Samples], [.. speed.Samples], [.. cadence.Samples]);

        private static void Add(TelemetryHistory history, DateTimeOffset capturedAt, double? value)
        {
            if (value is double measurement)
            {
                history.Add(new(capturedAt, measurement));
            }
        }
    }

    private void UpdateChartTimeRange()
    {
        if (isCustomChartRange)
        {
            return;
        }

        DateTimeOffset? latest = null;
        UpdateLatest(Telemetry.PowerChartSamples, ref latest);
        UpdateLatest(Telemetry.SpeedChartSamples, ref latest);
        UpdateLatest(Telemetry.CadenceChartSamples, ref latest);
        UpdateLatest(HeartRate.ChartSamples, ref latest);
        ChartTimeRangeEnd = latest;
        ChartTimeRangeStart = latest - ChartWindow;
        if (ChartTimeRangeStart is { } start && ChartTimeRangeEnd is { } end)
        {
            UpdatePreviousComparisonSamples(start, end);
        }
    }

    private void UpdatePreviousComparisonSamples(DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        if (previousSession is not { Samples.Length: > 0 } previous || !recordedSamples.TryPeek(out var currentStart))
        {
            PreviousPowerChartSamples = [];
            PreviousSpeedChartSamples = [];
            PreviousCadenceChartSamples = [];
            PreviousHeartRateChartSamples = [];
            return;
        }

        var previousStart = previous.Samples[0].CapturedAt;
        PreviousPowerChartSamples = MapPreviousSamples(previous.Samples, previousStart, currentStart.CapturedAt, rangeStart, rangeEnd, static sample => sample.PowerWatts);
        PreviousSpeedChartSamples = MapPreviousSamples(previous.Samples, previousStart, currentStart.CapturedAt, rangeStart, rangeEnd, static sample => sample.SpeedKilometersPerHour);
        PreviousCadenceChartSamples = MapPreviousSamples(previous.Samples, previousStart, currentStart.CapturedAt, rangeStart, rangeEnd, static sample => sample.CadenceRpm);
        PreviousHeartRateChartSamples = MapPreviousSamples(previous.Samples, previousStart, currentStart.CapturedAt, rangeStart, rangeEnd, static sample => sample.HeartRateBeatsPerMinute);
    }

    private static IReadOnlyList<TelemetrySample> MapPreviousSamples(
        IReadOnlyList<RecordedTelemetrySample> samples,
        DateTimeOffset previousStart,
        DateTimeOffset currentStart,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        Func<RecordedTelemetrySample, double?> valueSelector)
    {
        var mapped = new List<TelemetrySample>();
        foreach (var sample in samples)
        {
            var timestamp = currentStart + (sample.CapturedAt - previousStart);
            if (timestamp > rangeEnd)
            {
                break;
            }

            if (timestamp >= rangeStart && valueSelector(sample) is double value)
            {
                mapped.Add(new(timestamp, value));
            }
        }

        return mapped;
    }

    private static void UpdateLatest(IReadOnlyList<TelemetrySample> samples, ref DateTimeOffset? latest)
    {
        if (samples.Count > 0 && (latest is null || samples[^1].CapturedAt > latest))
        {
            latest = samples[^1].CapturedAt;
        }
    }

    private sealed record SessionTelemetry(IFtmsSession Session, TelemetrySnapshot Snapshot);

    private sealed record SessionTermination(IFtmsSession Session, SessionTerminationReason Reason);

    private sealed record HeartRateSessionTelemetry(IHeartRateSession Session, HeartRateObservation Observation);

    private sealed record HeartRateSessionTermination(IHeartRateSession Session, SessionTerminationReason Reason);

    private enum SessionTerminationReason
    {
        ConnectionLost,
        TelemetryFailure,
    }

    private enum ScanOutcome
    {
        Completed,
        Cancelled,
        Failed,
    }
}