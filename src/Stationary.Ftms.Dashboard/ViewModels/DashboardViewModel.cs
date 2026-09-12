using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using Stationary.Ftms;
using Stationary.Ftms.Dashboard.Core;
using Stationary.Ftms.Dashboard.Services;

namespace Stationary.Ftms.Dashboard.ViewModels;

public sealed class DashboardViewModel : ReactiveObject, IAsyncDisposable
{
    private const byte StopControlInformation = 0x01;
    private const byte PauseControlInformation = 0x02;
    private static readonly TimeSpan ChartWindow = TimeSpan.FromMinutes(5);

    private readonly IFtmsDiscoveryService discoveryService;
    private readonly IHeartRateDiscoveryService heartRateDiscoveryService;
    private readonly IScheduler timerScheduler;
    private readonly ObservableAsPropertyHelper<bool> isBusy;
    private readonly ObservableAsPropertyHelper<string> operationStatus;
    private readonly ObservableAsPropertyHelper<string> statusText;
    private readonly ObservableAsPropertyHelper<bool> canInitiateOperation;
    private readonly ObservableAsPropertyHelper<bool> isControlOperationExecuting;
    private readonly ObservableAsPropertyHelper<bool> isWorking;
    private readonly SerialDisposable sessionSubscriptions = new();
    private readonly SerialDisposable scanSubscriptions = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly ISubject<SessionTelemetry> telemetryIngress = Subject.Synchronize(new Subject<SessionTelemetry>());
    private readonly IObservable<TelemetrySnapshot> currentSessionTelemetry;
    private readonly ISubject<Unit> telemetrySessionReset = Subject.Synchronize(new Subject<Unit>());
    private readonly ISubject<SessionTermination> sessionTerminationIngress = Subject.Synchronize(new Subject<SessionTermination>());
    private readonly SerialDisposable heartRateSessionSubscriptions = new();
    private readonly SerialDisposable heartRateScanSubscriptions = new();
    private readonly ISubject<HeartRateSessionTelemetry> heartRateIngress = Subject.Synchronize(new Subject<HeartRateSessionTelemetry>());
    private readonly IObservable<HeartRateObservation> currentHeartRateTelemetry;
    private readonly ISubject<Unit> heartRateSessionReset = Subject.Synchronize(new Subject<Unit>());
    private readonly ISubject<HeartRateSessionTermination> heartRateSessionTerminationIngress = Subject.Synchronize(new Subject<HeartRateSessionTermination>());
    private readonly CompositeDisposable subscriptions = [];
    private string connectionStatus = "No fitness machine connected";
    private string heartRateConnectionStatus = "No external heart-rate sensor connected";
    private string controlStatus = "Connect a fitness machine to inspect FTMS controls.";
    private string physicalControlSynchronizationStatus = "Physical controls cannot be synchronized until a fitness machine is connected.";
    private PhysicalControlSynchronizationState physicalControlSynchronizationState;
    private FtmsDiscoveredDevice? selectedDevice;
    private HeartRateDiscoveredDevice? selectedHeartRateDevice;
    private bool isDevicePickerVisible;
    private bool isHeartRateDevicePickerVisible;
    private bool isConnected;
    private bool isHeartRateConnected;
    private bool isControllable;
    private bool hasControlPermission;
    private bool isWorkoutActive;
    private WorkoutSessionState workoutSessionState;
    private bool supportsTargetInclination;
    private bool supportsTargetPower;
    private bool supportsTargetResistance;
    private bool supportsWorkoutGoals;
    private string targetInclinationText = "0.0";
    private string targetPowerText = "100";
    private string targetResistanceText = "1.0";
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
    private bool isScanning;
    private bool isHeartRateScanning;
    private bool showUnavailableCapabilities;
    private DateTimeOffset? chartTimeRangeStart;
    private DateTimeOffset? chartTimeRangeEnd;
    private int isDisposed;

    private enum WorkoutSessionState
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

    public DashboardViewModel(IFtmsDiscoveryService discoveryService, IHeartRateDiscoveryService heartRateDiscoveryService)
        : this(discoveryService, heartRateDiscoveryService, RxApp.TaskpoolScheduler)
    {
    }

    internal DashboardViewModel(IFtmsDiscoveryService discoveryService, IHeartRateDiscoveryService heartRateDiscoveryService, IScheduler timerScheduler)
    {
        this.discoveryService = discoveryService;
        this.heartRateDiscoveryService = heartRateDiscoveryService;
        this.timerScheduler = timerScheduler;
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
        Telemetry = new();
        HeartRate = new();
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
                    HeartRate.Present(current);
                }
                else
                {
                    HeartRate.Clear();
                }

                UpdateChartTimeRange();
            }, HandleHeartRatePipelineError));
        var canScan = this.WhenAnyValue(
                viewModel => viewModel.IsScanning,
                viewModel => viewModel.IsHeartRateScanning,
                (scanning, heartRateScanning) => !scanning && !heartRateScanning)
            .ObserveOn(RxApp.MainThreadScheduler);
        var canConnect = this.WhenAnyValue(
                viewModel => viewModel.SelectedDevice,
                viewModel => viewModel.IsConnected,
                viewModel => viewModel.IsHeartRateScanning,
                (device, connected, heartRateScanning) => device is not null && !connected && !heartRateScanning)
            .ObserveOn(RxApp.MainThreadScheduler);
        var canHeartRateConnect = this.WhenAnyValue(
                viewModel => viewModel.SelectedHeartRateDevice,
                viewModel => viewModel.IsHeartRateConnected,
                viewModel => viewModel.IsScanning,
                (device, connected, scanning) => device is not null && !connected && !scanning)
            .ObserveOn(RxApp.MainThreadScheduler);
        var canDisconnect = this.WhenAnyValue(viewModel => viewModel.IsConnected).ObserveOn(RxApp.MainThreadScheduler);
        var canHeartRateDisconnect = this.WhenAnyValue(viewModel => viewModel.IsHeartRateConnected).ObserveOn(RxApp.MainThreadScheduler);
        var canResetSession = this.WhenAnyValue(viewModel => viewModel.IsConnected, viewModel => viewModel.IsHeartRateConnected, (fitnessMachineConnected, heartRateConnected) => fitnessMachineConnected || heartRateConnected).ObserveOn(RxApp.MainThreadScheduler);
        var canRequestControl = this.WhenAnyValue(viewModel => viewModel.IsControllable, viewModel => viewModel.HasControlPermission, (controllable, hasPermission) => controllable && !hasPermission).ObserveOn(RxApp.MainThreadScheduler);
        var canStart = this.WhenAnyValue(viewModel => viewModel.IsControllable, viewModel => viewModel.IsWorkoutActive, (controllable, active) => controllable && !active).ObserveOn(RxApp.MainThreadScheduler);
        var canStop = this.WhenAnyValue(viewModel => viewModel.IsControllable, viewModel => viewModel.IsWorkoutActive, (controllable, active) => controllable && active).ObserveOn(RxApp.MainThreadScheduler);
        var canApplyTarget = this.WhenAnyValue(viewModel => viewModel.IsControllable).ObserveOn(RxApp.MainThreadScheduler);
        ScanCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(_ => ScanAsync(), cancellationToken), canScan);
        ConnectCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(ConnectAsync, cancellationToken), canConnect);
        DisconnectCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(_ => DisconnectAsync(), cancellationToken), canDisconnect);
        ScanHeartRateCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(_ => ScanHeartRateAsync(), cancellationToken), canScan);
        ConnectHeartRateCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(ConnectHeartRateAsync, cancellationToken), canHeartRateConnect);
        DisconnectHeartRateCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(_ => DisconnectHeartRateAsync(), cancellationToken), canHeartRateDisconnect);
        RequestControlCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(RequestControlAsync, cancellationToken), canRequestControl);
        StartCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(StartWorkoutAsync, cancellationToken), canStart);
        StopCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(PauseWorkoutAsync, cancellationToken), canStop);
        SetTargetInclinationCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(SetTargetInclinationAsync, cancellationToken), canApplyTarget);
        SetTargetPowerCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(SetTargetPowerAsync, cancellationToken), canApplyTarget);
        SetTargetResistanceCommand = ReactiveCommand.CreateFromTask(cancellationToken => ExecuteExclusiveAsync(SetTargetResistanceAsync, cancellationToken), canApplyTarget);
        ResetSessionCommand = ReactiveCommand.Create(ResetSession, canResetSession);
        ExportSessionCommand = ReactiveCommand.CreateFromTask(ExportSessionAsync, canResetSession);
        ApplyHeartRateZonesCommand = ReactiveCommand.Create(ApplyHeartRateZones);
        ToggleUnavailableCapabilitiesCommand = ReactiveCommand.Create(() =>
        {
            ShowUnavailableCapabilities = !ShowUnavailableCapabilities;
        });
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
        isBusy = Observable.CombineLatest(
                ScanCommand.IsExecuting,
                ConnectCommand.IsExecuting,
                DisconnectCommand.IsExecuting,
                RequestControlCommand.IsExecuting,
                StartCommand.IsExecuting,
                StopCommand.IsExecuting,
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
        canInitiateOperation = this.WhenAnyValue(viewModel => viewModel.IsBusy)
            .Select(busy => !busy)
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.CanInitiateOperation);
        operationStatus = Observable.CombineLatest(
                ScanCommand.IsExecuting.Select(executing => executing ? "Searching for FTMS fitness machines..." : null),
                ConnectCommand.IsExecuting.Select(executing => executing ? "Connecting to the fitness machine..." : null),
                DisconnectCommand.IsExecuting.Select(executing => executing ? "Disconnecting from the fitness machine..." : null),
                RequestControlCommand.IsExecuting.Select(executing => executing ? "Requesting control from the fitness machine..." : null),
                StartCommand.IsExecuting.Select(executing => executing ? "Starting the fitness machine session..." : null),
                StopCommand.IsExecuting.Select(executing => executing ? "Pausing the fitness machine session..." : null),
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
        statusText = this.WhenAnyValue(
                viewModel => viewModel.ConnectionStatus,
                viewModel => viewModel.OperationStatus,
                (connection, operation) => string.IsNullOrEmpty(operation) ? connection : operation)
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.StatusText);

        isControlOperationExecuting = Observable.CombineLatest(
                RequestControlCommand.IsExecuting,
                StartCommand.IsExecuting,
                StopCommand.IsExecuting)
            .Select(states => states.Any(static executing => executing))
            .DistinctUntilChanged()
            .ToProperty(this, viewModel => viewModel.IsControlOperationExecuting);
        isWorking = this.WhenAnyValue(
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
    }

    public string ConnectionStatus
    {
        get => connectionStatus;
        private set => this.RaiseAndSetIfChanged(ref connectionStatus, value);
    }

    public string HeartRateConnectionStatus
    {
        get => heartRateConnectionStatus;
        private set => this.RaiseAndSetIfChanged(ref heartRateConnectionStatus, value);
    }

    public string ControlStatus
    {
        get => controlStatus;
        private set => this.RaiseAndSetIfChanged(ref controlStatus, value);
    }

    public ObservableCollection<FtmsDiscoveredDevice> DiscoveredDevices { get; }

    public ObservableCollection<HeartRateDiscoveredDevice> HeartRateDiscoveredDevices { get; }

    public ObservableCollection<DeviceCapability> DeviceCapabilities { get; }

    public ObservableCollection<DeviceCapability> LiveCapabilities { get; }

    public ObservableCollection<DeviceCapability> AvailableControlCapabilities { get; }

    public ObservableCollection<DeviceCapability> UnavailableCapabilities { get; }

    public TelemetryPresentationViewModel Telemetry { get; }

    public HeartRatePresentationViewModel HeartRate { get; }

    public DateTimeOffset? ChartTimeRangeStart
    {
        get => chartTimeRangeStart;
        private set => this.RaiseAndSetIfChanged(ref chartTimeRangeStart, value);
    }

    public DateTimeOffset? ChartTimeRangeEnd
    {
        get => chartTimeRangeEnd;
        private set => this.RaiseAndSetIfChanged(ref chartTimeRangeEnd, value);
    }

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

    public FtmsDiscoveredDevice? SelectedDevice
    {
        get => selectedDevice;
        set => this.RaiseAndSetIfChanged(ref selectedDevice, value);
    }

    public HeartRateDiscoveredDevice? SelectedHeartRateDevice
    {
        get => selectedHeartRateDevice;
        set => this.RaiseAndSetIfChanged(ref selectedHeartRateDevice, value);
    }

    public bool IsDevicePickerVisible
    {
        get => isDevicePickerVisible;
        private set => this.RaiseAndSetIfChanged(ref isDevicePickerVisible, value);
    }

    public bool IsHeartRateDevicePickerVisible
    {
        get => isHeartRateDevicePickerVisible;
        private set => this.RaiseAndSetIfChanged(ref isHeartRateDevicePickerVisible, value);
    }

    public bool IsConnected
    {
        get => isConnected;
        private set
        {
            if (this.RaiseAndSetIfChanged(ref isConnected, value))
            {
                this.RaisePropertyChanged(nameof(IsDisconnected));
                this.RaisePropertyChanged(nameof(IsSessionAvailable));
                this.RaisePropertyChanged(nameof(IsSessionUnavailable));
            }
        }
    }

    public bool IsDisconnected => !IsConnected;

    public bool IsHeartRateConnected
    {
        get => isHeartRateConnected;
        private set
        {
            if (this.RaiseAndSetIfChanged(ref isHeartRateConnected, value))
            {
                this.RaisePropertyChanged(nameof(IsHeartRateDisconnected));
                this.RaisePropertyChanged(nameof(IsSessionAvailable));
                this.RaisePropertyChanged(nameof(IsSessionUnavailable));
            }
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
            if (this.RaiseAndSetIfChanged(ref isControllable, value))
            {
                this.RaisePropertyChanged(nameof(ControlAccessText));
                this.RaisePropertyChanged(nameof(IsControlPermissionRequired));
                this.RaisePropertyChanged(nameof(AreTargetControlsAvailable));
            }
        }
    }

    public bool HasControlPermission
    {
        get => hasControlPermission;
        private set
        {
            if (this.RaiseAndSetIfChanged(ref hasControlPermission, value))
            {
                this.RaisePropertyChanged(nameof(ControlAccessText));
                this.RaisePropertyChanged(nameof(AreTargetControlsAvailable));
                this.RaisePropertyChanged(nameof(MachineStateText));
                this.RaisePropertyChanged(nameof(StartActionText));
                this.RaisePropertyChanged(nameof(IsControlPermissionRequired));
            }
        }
    }

    public bool IsWorkoutActive
    {
        get => isWorkoutActive;
        private set
        {
            if (this.RaiseAndSetIfChanged(ref isWorkoutActive, value))
            {
                this.RaisePropertyChanged(nameof(MachineStateText));
                this.RaisePropertyChanged(nameof(StartActionText));
            }
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

    public bool AreTargetControlsAvailable => IsControllable && TargetControls.Count > 0;

    public string PhysicalControlSynchronizationStateText => physicalControlSynchronizationState switch
    {
        PhysicalControlSynchronizationState.Listening => "Listening",
        PhysicalControlSynchronizationState.Synchronized => "Synchronized",
        _ => "Unavailable",
    };

    public string PhysicalControlSynchronizationStatus
    {
        get => physicalControlSynchronizationStatus;
        private set => this.RaiseAndSetIfChanged(ref physicalControlSynchronizationStatus, value);
    }

    public bool HasPowerTargetControl => PowerTargetControl is not null;

    public bool HasManualTargetControls => ManualTargetControls.Count > 0;

    public bool IsBusy => isBusy.Value;

    public string OperationStatus => operationStatus.Value;

    public string StatusText => statusText.Value;

    public bool CanInitiateOperation => canInitiateOperation.Value;

    public bool IsControlOperationExecuting => isControlOperationExecuting.Value;

    public bool IsWorking => isWorking.Value;

    public bool IsScanning
    {
        get => isScanning;
        private set => this.RaiseAndSetIfChanged(ref isScanning, value);
    }

    public bool IsHeartRateScanning
    {
        get => isHeartRateScanning;
        private set => this.RaiseAndSetIfChanged(ref isHeartRateScanning, value);
    }

    public bool ShowUnavailableCapabilities
    {
        get => showUnavailableCapabilities;
        private set => this.RaiseAndSetIfChanged(ref showUnavailableCapabilities, value);
    }

    public bool SupportsTargetPower
    {
        get => supportsTargetPower;
        private set => this.RaiseAndSetIfChanged(ref supportsTargetPower, value);
    }

    public bool SupportsTargetInclination
    {
        get => supportsTargetInclination;
        private set => this.RaiseAndSetIfChanged(ref supportsTargetInclination, value);
    }

    public bool SupportsTargetResistance
    {
        get => supportsTargetResistance;
        private set => this.RaiseAndSetIfChanged(ref supportsTargetResistance, value);
    }

    public bool SupportsWorkoutGoals
    {
        get => supportsWorkoutGoals;
        private set => this.RaiseAndSetIfChanged(ref supportsWorkoutGoals, value);
    }

    public bool HasAdjustableTargets => SupportsTargetPower || SupportsTargetResistance || SupportsWorkoutGoals;

    public string TargetPowerText
    {
        get => targetPowerText;
        set => this.RaiseAndSetIfChanged(ref targetPowerText, value);
    }

    public string TargetInclinationText
    {
        get => targetInclinationText;
        set => this.RaiseAndSetIfChanged(ref targetInclinationText, value);
    }

    public string TargetResistanceText
    {
        get => targetResistanceText;
        set => this.RaiseAndSetIfChanged(ref targetResistanceText, value);
    }

    public ReactiveCommand<Unit, Unit> ScanCommand { get; }

    public ReactiveCommand<Unit, Unit> ConnectCommand { get; }

    public ReactiveCommand<Unit, Unit> DisconnectCommand { get; }

    public ReactiveCommand<Unit, Unit> ScanHeartRateCommand { get; }

    public ReactiveCommand<Unit, Unit> ConnectHeartRateCommand { get; }

    public ReactiveCommand<Unit, Unit> DisconnectHeartRateCommand { get; }

    public ReactiveCommand<Unit, Unit> RequestControlCommand { get; }

    public ReactiveCommand<Unit, Unit> StartCommand { get; }

    public ReactiveCommand<Unit, Unit> StopCommand { get; }

    public ReactiveCommand<Unit, Unit> SetTargetPowerCommand { get; }

    public ReactiveCommand<Unit, Unit> SetTargetInclinationCommand { get; }

    public ReactiveCommand<Unit, Unit> SetTargetResistanceCommand { get; }

    public ReactiveCommand<Unit, Unit> ResetSessionCommand { get; }

    public ReactiveCommand<Unit, Unit> ExportSessionCommand { get; }

    public ReactiveCommand<Unit, Unit> ApplyHeartRateZonesCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleUnavailableCapabilitiesCommand { get; }

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
        HeartRate.Dispose();
        isBusy.Dispose();
        operationStatus.Dispose();
        statusText.Dispose();
        canInitiateOperation.Dispose();
        isControlOperationExecuting.Dispose();
        isWorking.Dispose();
        telemetryIngress.OnCompleted();
        telemetrySessionReset.OnCompleted();
        sessionTerminationIngress.OnCompleted();
        heartRateIngress.OnCompleted();
        heartRateSessionReset.OnCompleted();
        heartRateSessionTerminationIngress.OnCompleted();
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
        SetTargetInclinationCommand.Dispose();
        SetTargetPowerCommand.Dispose();
        SetTargetResistanceCommand.Dispose();
        ResetSessionCommand.Dispose();
        ExportSessionCommand.Dispose();
        ApplyHeartRateZonesCommand.Dispose();
        ToggleUnavailableCapabilitiesCommand.Dispose();
        DisposeTargetControls();
    }

    private async Task ScanAsync()
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

    private async Task ScanHeartRateAsync()
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

    private async Task ConnectAsync(CancellationToken cancellationToken)
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

    private async Task ConnectHeartRateAsync(CancellationToken cancellationToken)
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

    private async Task DisconnectAsync()
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

    private async Task DisconnectHeartRateAsync()
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

    private async Task ExportSessionAsync(CancellationToken cancellationToken)
    {
        var fileName = $"stationary-session-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.csv";
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        var csv = string.Join(Environment.NewLine, Telemetry.CreateSessionCsv(), HeartRate.CreateSessionCsvRows());
        await File.WriteAllTextAsync(path, csv, cancellationToken);
        ConnectionStatus = $"Session summary exported to {path}.";
    }

    private void ResetSession()
    {
        Telemetry.ResetSession();
        HeartRate.ResetSession();
    }

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

    private async Task SetTargetPowerAsync(CancellationToken cancellationToken)
    {
        if (!short.TryParse(TargetPowerText, out var watts))
        {
            return;
        }

        await SetTargetPowerFromSliderAsync(watts, cancellationToken);
    }

    private async Task SetTargetInclinationAsync(CancellationToken cancellationToken)
    {
        if (!TryGetInclinationTenths(TargetInclinationText, out var tenths))
        {
            return;
        }

        await SetTargetInclinationFromSliderAsync(tenths / 10d, cancellationToken);
    }

    private async Task SetTargetResistanceAsync(CancellationToken cancellationToken)
    {
        if (!TryGetResistanceTenths(TargetResistanceText, out var tenths))
        {
            return;
        }

        await SetTargetResistanceFromSliderAsync(tenths / 10d, cancellationToken);
    }

    private async Task RequestControlAsync(CancellationToken cancellationToken)
    {
        await EnsureControlPermissionAsync(cancellationToken);
    }

    private async Task StartWorkoutAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureControlPermissionAsync(cancellationToken))
        {
            return;
        }

        if (await ExecuteControlAsync(FtmsControlPointOpcode.StartOrResume, Array.Empty<byte>(), "Session started", cancellationToken))
        {
            StartOrResumeWorkoutSession();
            ControlStatus = "Session running. Adjust targets as needed.";
        }
    }

    private async Task PauseWorkoutAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureControlPermissionAsync(cancellationToken))
        {
            return;
        }

        if (await ExecuteControlAsync(FtmsControlPointOpcode.StopOrPause, new byte[] { PauseControlInformation }, "Session paused", cancellationToken))
        {
            PauseWorkoutSession();
            ControlStatus = "Session paused. Target adjustments remain available.";
        }
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
                Telemetry.StartSession(timestamp);
                HeartRate.StartSession();
                break;
            case WorkoutSessionState.Paused:
                Telemetry.ResumeSession(timestamp);
                HeartRate.ResumeSession();
                break;
        }

        SetWorkoutSessionState(WorkoutSessionState.Running);
        IsWorkoutActive = true;
    }

    private void PauseWorkoutSession()
    {
        var timestamp = DateTimeOffset.UtcNow;
        Telemetry.PauseSession(timestamp);
        HeartRate.PauseSession(timestamp);
        SetWorkoutSessionState(WorkoutSessionState.Paused);
        IsWorkoutActive = false;
    }

    private void StopWorkoutSession()
    {
        var timestamp = DateTimeOffset.UtcNow;
        Telemetry.PauseSession(timestamp);
        HeartRate.PauseSession(timestamp);
        SetWorkoutSessionState(WorkoutSessionState.Stopped);
        IsWorkoutActive = false;
    }

    private void SetWorkoutSessionState(WorkoutSessionState value)
    {
        if (workoutSessionState == value)
        {
            return;
        }

        workoutSessionState = value;
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
            await MainThread.InvokeOnMainThreadAsync(() => CompleteScan(cancellation, outcome)).ConfigureAwait(false);
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
            await MainThread.InvokeOnMainThreadAsync(() => CompleteHeartRateScan(cancellation, outcome)).ConfigureAwait(false);
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

    private void Present(TelemetrySnapshot snapshot) => Telemetry.Present(snapshot);

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
        telemetryCancellation = null;
        telemetryConsumer = null;
        session = null;
        IsConnected = false;
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
        DateTimeOffset? latest = null;
        UpdateLatest(Telemetry.PowerChartSamples, ref latest);
        UpdateLatest(Telemetry.SpeedChartSamples, ref latest);
        UpdateLatest(Telemetry.CadenceChartSamples, ref latest);
        UpdateLatest(HeartRate.ChartSamples, ref latest);
        ChartTimeRangeEnd = latest;
        ChartTimeRangeStart = latest - ChartWindow;
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