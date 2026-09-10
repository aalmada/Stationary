using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

using Stationary.Ftms;
using Stationary.Ftms.Client;
using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Services;

public sealed class PluginBleFtmsSession : IFtmsSession
{
    private readonly IAdapter adapter;
    private readonly IDevice device;
    private readonly ICharacteristic telemetryCharacteristic;
    private readonly EventHandler<CharacteristicUpdatedEventArgs> valueUpdated;
    private readonly ICharacteristic? controlPointCharacteristic;
    private readonly EventHandler<CharacteristicUpdatedEventArgs>? controlPointUpdated;
    private readonly FtmsControlPointCoordinator? controlPointCoordinator;
    private readonly ICharacteristic? machineStatusCharacteristic;
    private readonly EventHandler<CharacteristicUpdatedEventArgs>? machineStatusUpdated;
    private readonly bool isMachineStatusSubscribed;
    private readonly EventHandler<DeviceErrorEventArgs> connectionLost;
    private int disposed;
    private long telemetryPacketsReceived;
    private long machineStatusPacketsReceived;
    private FtmsMachineStatusChangedEventArgs? latestMachineStatus;

    public PluginBleFtmsSession(
        IAdapter adapter,
        IDevice device,
        ICharacteristic telemetryCharacteristic,
        FtmsMachineDataType machineType,
        IReadOnlyList<FtmsMachineDataType> advertisedMachineTypes,
        EventHandler<CharacteristicUpdatedEventArgs> valueUpdated,
        LatestTelemetryBuffer telemetry,
        FitnessMachineFeature? features,
        ICharacteristic? controlPointCharacteristic,
        EventHandler<CharacteristicUpdatedEventArgs>? controlPointUpdated,
        FtmsControlPointCoordinator? controlPointCoordinator,
        string controlPointAvailability,
        FtmsSupportedRanges supportedRanges,
        ICharacteristic? machineStatusCharacteristic,
        EventHandler<CharacteristicUpdatedEventArgs>? machineStatusUpdated,
        bool isMachineStatusSubscribed,
        string machineStatusAvailability)
    {
        this.adapter = adapter;
        this.device = device;
        this.telemetryCharacteristic = telemetryCharacteristic;
        this.valueUpdated = valueUpdated;
        Features = features;
        this.controlPointCharacteristic = controlPointCharacteristic;
        this.controlPointUpdated = controlPointUpdated;
        this.controlPointCoordinator = controlPointCoordinator;
        ControlPointAvailability = controlPointAvailability;
        SupportedRanges = supportedRanges;
        this.machineStatusCharacteristic = machineStatusCharacteristic;
        this.machineStatusUpdated = machineStatusUpdated;
        this.isMachineStatusSubscribed = isMachineStatusSubscribed;
        MachineStatusAvailability = machineStatusAvailability;
        MachineType = machineType;
        AdvertisedMachineTypes = advertisedMachineTypes;
        Telemetry = telemetry;
        connectionLost = (_, eventArgs) =>
        {
            if (eventArgs.Device.Id == device.Id)
            {
                Telemetry.Complete();
                ConnectionLost?.Invoke(this, EventArgs.Empty);
            }
        };
        adapter.DeviceConnectionLost += connectionLost;
    }

    public string DeviceName => device.Name ?? device.Id.ToString();

    public FtmsMachineDataType MachineType { get; }

    public IReadOnlyList<FtmsMachineDataType> AdvertisedMachineTypes { get; }

    public long TelemetryPacketsReceived => Interlocked.Read(ref telemetryPacketsReceived);

    public FitnessMachineFeature? Features { get; }

    public FtmsSupportedRanges SupportedRanges { get; }

    public bool CanControl => controlPointCoordinator is not null;

    public string ControlPointAvailability { get; }

    public bool IsMachineStatusSubscribed => isMachineStatusSubscribed;

    public string MachineStatusAvailability { get; }

    public long MachineStatusPacketsReceived => Interlocked.Read(ref machineStatusPacketsReceived);

    public FtmsMachineStatusChangedEventArgs? LatestMachineStatus => Volatile.Read(ref latestMachineStatus);

    public LatestTelemetryBuffer Telemetry { get; }

    public event EventHandler? TelemetryReceived;

    public event EventHandler? ConnectionLost;

    public event EventHandler<FtmsMachineStatusChangedEventArgs>? MachineStatusChanged;

    public void RecordTelemetry(FtmsDecodeStatus status)
    {
        if (status == FtmsDecodeStatus.Success)
        {
            Interlocked.Increment(ref telemetryPacketsReceived);
            TelemetryReceived?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RecordMachineStatus(ReadOnlySpan<byte> payload)
    {
        if (FitnessMachineStatus.TryDecode(payload, out var opcode, out var parameters) == FtmsDecodeStatus.Success)
        {
            var eventArgs = new FtmsMachineStatusChangedEventArgs(opcode, parameters.ToArray());
            Volatile.Write(ref latestMachineStatus, eventArgs);
            Interlocked.Increment(ref machineStatusPacketsReceived);
            MachineStatusChanged?.Invoke(this, eventArgs);
        }
    }

    public ValueTask<FtmsControlPointResult> ExecuteControlAsync(
        FtmsControlPointOpcode opcode,
        ReadOnlyMemory<byte> parameters,
        CancellationToken cancellationToken)
        => controlPointCoordinator is null
            ? ValueTask.FromException<FtmsControlPointResult>(new InvalidOperationException("This fitness machine does not expose a usable FTMS Control Point."))
            : controlPointCoordinator.ExecuteAsync(opcode, parameters, TimeSpan.FromSeconds(8), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        adapter.DeviceConnectionLost -= connectionLost;
        telemetryCharacteristic.ValueUpdated -= valueUpdated;
        if (controlPointCharacteristic is not null && controlPointUpdated is not null)
        {
            controlPointCharacteristic.ValueUpdated -= controlPointUpdated;
        }

        if (machineStatusCharacteristic is not null && machineStatusUpdated is not null)
        {
            machineStatusCharacteristic.ValueUpdated -= machineStatusUpdated;
        }

        try
        {
            await telemetryCharacteristic.StopUpdatesAsync().ConfigureAwait(false);
            if (controlPointCharacteristic is not null)
            {
                await controlPointCharacteristic.StopUpdatesAsync().ConfigureAwait(false);
            }

            if (machineStatusCharacteristic is not null)
            {
                await machineStatusCharacteristic.StopUpdatesAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            controlPointCoordinator?.Dispose();
            Telemetry.Complete();
            await adapter.DisconnectDeviceAsync(device).ConfigureAwait(false);
        }
    }
}