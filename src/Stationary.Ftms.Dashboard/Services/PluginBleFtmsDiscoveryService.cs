using System.Collections.Concurrent;

using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

using Stationary.Ble;
using Stationary.Ftms.Client;
using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Services;

public sealed class PluginBleFtmsDiscoveryService : IFtmsDiscoveryService
{
    private static readonly Guid FitnessMachineServiceId = BluetoothUuid.FromAssignedNumber(0x1826);
    private static readonly Guid FitnessMachineFeatureId = BluetoothUuid.FromAssignedNumber(0x2ACC);
    private static readonly Guid FitnessMachineControlPointId = BluetoothUuid.FromAssignedNumber(0x2AD9);
    private static readonly Guid FitnessMachineStatusId = BluetoothUuid.FromAssignedNumber(0x2ADA);
    private static readonly Guid SupportedSpeedRangeId = BluetoothUuid.FromAssignedNumber(0x2AD4);
    private static readonly Guid SupportedInclinationRangeId = BluetoothUuid.FromAssignedNumber(0x2AD5);
    private static readonly Guid SupportedResistanceLevelRangeId = BluetoothUuid.FromAssignedNumber(0x2AD6);
    private static readonly Guid SupportedHeartRateRangeId = BluetoothUuid.FromAssignedNumber(0x2AD7);
    private static readonly Guid SupportedPowerRangeId = BluetoothUuid.FromAssignedNumber(0x2AD8);
    private readonly IAdapter adapter = CrossBluetoothLE.Current.Adapter;
    private readonly ConcurrentDictionary<Guid, IDevice> discoveredDevices = [];

    public async ValueTask DiscoverAsync(Action<FtmsDiscoveredDevice> deviceDiscovered, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceDiscovered);

        EventHandler<DeviceEventArgs>? discovered = (_, eventArgs) =>
        {
            if (!discoveredDevices.TryAdd(eventArgs.Device.Id, eventArgs.Device))
            {
                discoveredDevices[eventArgs.Device.Id] = eventArgs.Device;
                return;
            }

            deviceDiscovered(new FtmsDiscoveredDevice(
                eventArgs.Device.Id.ToString(),
                eventArgs.Device.Name ?? "Unnamed Bluetooth device",
                "FTMS device"));
        };

        discoveredDevices.Clear();
        adapter.DeviceDiscovered += discovered;
        try
        {
            adapter.ScanMode = ScanMode.LowLatency;
            adapter.ScanTimeout = 20_000;
            await adapter.StartScanningForDevicesAsync([FitnessMachineServiceId], _ => true, false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            adapter.DeviceDiscovered -= discovered;
            await adapter.StopScanningForDevicesAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<IFtmsSession?> ConnectAsync(FtmsDiscoveredDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!Guid.TryParse(device.Id, out var deviceId))
        {
            return null;
        }

        return !discoveredDevices.TryGetValue(deviceId, out var knownDevice)
            ? null
            : await TryConnectAsync(knownDevice, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IFtmsSession?> TryConnectAsync(IDevice device, CancellationToken cancellationToken)
    {
        ICharacteristic? telemetryCharacteristic = null;
        EventHandler<CharacteristicUpdatedEventArgs>? valueUpdated = null;
        ICharacteristic? controlPoint = null;
        FtmsControlPointCoordinator? controlPointCoordinator = null;
        EventHandler<CharacteristicUpdatedEventArgs>? controlPointUpdated = null;
        ICharacteristic? machineStatus = null;
        EventHandler<CharacteristicUpdatedEventArgs>? machineStatusUpdated = null;
        var isMachineStatusSubscribed = false;
        var machineStatusAvailability = "Fitness Machine Status (0x2ADA) is not exposed. Physical controls cannot be synchronized.";
        var pendingMachineStatusPayloads = new Queue<byte[]>();
        var machineStatusGate = new object();
        try
        {
            await adapter.ConnectToDeviceAsync(device, cancellationToken: cancellationToken).ConfigureAwait(false);
            var service = await device.GetServiceAsync(FitnessMachineServiceId).ConfigureAwait(false);
            var characteristics = await service.GetCharacteristicsAsync().ConfigureAwait(false);
            var machineData = FindMachineDataCharacteristic(characteristics);
            if (machineData is null)
            {
                await DisconnectQuietlyAsync(device).ConfigureAwait(false);
                return null;
            }

            var buffer = new LatestTelemetryBuffer();
            var processor = new FtmsTelemetryProcessor(machineData.Value.MachineType, buffer);
            PluginBleFtmsSession? session = null;
            telemetryCharacteristic = machineData.Value.Characteristic;
            valueUpdated = (_, eventArgs) =>
                session?.RecordTelemetry(processor.Process(eventArgs.Characteristic.Value, DateTimeOffset.UtcNow));
            telemetryCharacteristic.ValueUpdated += valueUpdated;
            await telemetryCharacteristic.StartUpdatesAsync(cancellationToken).ConfigureAwait(false);

            var features = await ReadFeaturesAsync(service).ConfigureAwait(false);
            var supportedRanges = await ReadSupportedRangesAsync(service, cancellationToken).ConfigureAwait(false);

            var controlPointAvailability = "FTMS Control Point (0x2AD9) is not exposed by this device.";
            try
            {
                var candidate = characteristics.FirstOrDefault(characteristic => characteristic.Id == FitnessMachineControlPointId);
                if (candidate is not null)
                {
                    controlPointCoordinator = new(new PluginBleControlPointTransport(candidate));
                    controlPointUpdated = (_, eventArgs) => controlPointCoordinator.HandleIndication(eventArgs.Characteristic.Value);
                    controlPoint = candidate;
                    candidate.ValueUpdated += controlPointUpdated;
                    await candidate.StartUpdatesAsync(cancellationToken).ConfigureAwait(false);
                    controlPointAvailability = "FTMS Control Point ready.";
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (controlPointUpdated is not null)
                {
                    controlPoint?.ValueUpdated -= controlPointUpdated;
                }

                await StopUpdatesQuietlyAsync(controlPoint).ConfigureAwait(false);
                controlPointCoordinator?.Dispose();
                controlPointCoordinator = null;
                controlPointUpdated = null;
                controlPoint = null;
                controlPointAvailability = $"FTMS Control Point setup failed: {exception.Message}";
            }

            try
            {
                var candidate = characteristics.FirstOrDefault(characteristic => characteristic.Id == FitnessMachineStatusId);
                if (candidate is null)
                {
                    machineStatusAvailability = controlPoint is null
                        ? "Fitness Machine Status (0x2ADA) is not exposed. Physical controls cannot be synchronized."
                        : "Fitness Machine Status (0x2ADA) is missing even though the FTMS Control Point is available. Physical controls cannot be synchronized.";
                }
                else if (!candidate.CanUpdate)
                {
                    machineStatusAvailability = "Fitness Machine Status (0x2ADA) is exposed but does not support notifications. Physical controls cannot be synchronized.";
                }
                else
                {
                    machineStatus = candidate;
                    machineStatusUpdated = (_, eventArgs) =>
                    {
                        lock (machineStatusGate)
                        {
                            if (session is { } activeSession)
                            {
                                activeSession.RecordMachineStatus(eventArgs.Characteristic.Value);
                            }
                            else
                            {
                                pendingMachineStatusPayloads.Enqueue([.. eventArgs.Characteristic.Value]);
                            }
                        }
                    };
                    candidate.ValueUpdated += machineStatusUpdated;
                    await candidate.StartUpdatesAsync(cancellationToken).ConfigureAwait(false);
                    isMachineStatusSubscribed = true;
                    machineStatusAvailability = "Fitness Machine Status (0x2ADA) is subscribed. Waiting for the bike to report a status change.";
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (machineStatusUpdated is not null)
                {
                    machineStatus?.ValueUpdated -= machineStatusUpdated;
                }

                await StopUpdatesQuietlyAsync(machineStatus).ConfigureAwait(false);
                machineStatus = null;
                machineStatusUpdated = null;
                machineStatusAvailability = $"Fitness Machine Status (0x2ADA) subscription failed: {exception.Message}";
            }

            lock (machineStatusGate)
            {
                session = new PluginBleFtmsSession(
                    adapter,
                    device,
                    telemetryCharacteristic,
                    machineData.Value.MachineType,
                    machineData.Value.AdvertisedMachineTypes,
                    valueUpdated,
                    buffer,
                    features,
                    controlPoint,
                    controlPointUpdated,
                    controlPointCoordinator,
                    controlPointAvailability,
                    supportedRanges,
                    machineStatus,
                    machineStatusUpdated,
                    isMachineStatusSubscribed,
                    machineStatusAvailability);
                while (pendingMachineStatusPayloads.Count > 0)
                {
                    session.RecordMachineStatus(pendingMachineStatusPayloads.Dequeue());
                }
            }

            return session;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupFailedConnectionAsync(device, telemetryCharacteristic, valueUpdated, controlPoint, controlPointUpdated, controlPointCoordinator, machineStatus, machineStatusUpdated).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await CleanupFailedConnectionAsync(device, telemetryCharacteristic, valueUpdated, controlPoint, controlPointUpdated, controlPointCoordinator, machineStatus, machineStatusUpdated).ConfigureAwait(false);
            return null;
        }
    }

    private async ValueTask CleanupFailedConnectionAsync(
        IDevice device,
        ICharacteristic? telemetryCharacteristic,
        EventHandler<CharacteristicUpdatedEventArgs>? valueUpdated,
        ICharacteristic? controlPoint,
        EventHandler<CharacteristicUpdatedEventArgs>? controlPointUpdated,
        FtmsControlPointCoordinator? controlPointCoordinator,
        ICharacteristic? machineStatus,
        EventHandler<CharacteristicUpdatedEventArgs>? machineStatusUpdated)
    {
        if (telemetryCharacteristic is not null && valueUpdated is not null)
        {
            telemetryCharacteristic.ValueUpdated -= valueUpdated;
        }

        if (controlPoint is not null && controlPointUpdated is not null)
        {
            controlPoint.ValueUpdated -= controlPointUpdated;
        }

        if (machineStatus is not null && machineStatusUpdated is not null)
        {
            machineStatus.ValueUpdated -= machineStatusUpdated;
        }

        controlPointCoordinator?.Dispose();
        await StopUpdatesQuietlyAsync(telemetryCharacteristic).ConfigureAwait(false);
        await StopUpdatesQuietlyAsync(controlPoint).ConfigureAwait(false);
        await StopUpdatesQuietlyAsync(machineStatus).ConfigureAwait(false);
        await DisconnectQuietlyAsync(device).ConfigureAwait(false);
    }

    private static async ValueTask StopUpdatesQuietlyAsync(ICharacteristic? characteristic)
    {
        if (characteristic is null)
        {
            return;
        }

        try
        {
            await characteristic.StopUpdatesAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async ValueTask DisconnectQuietlyAsync(IDevice device)
    {
        try
        {
            await adapter.DisconnectDeviceAsync(device).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static (ICharacteristic Characteristic, FtmsMachineDataType MachineType, IReadOnlyList<FtmsMachineDataType> AdvertisedMachineTypes)? FindMachineDataCharacteristic(IReadOnlyList<ICharacteristic> characteristics)
    {
        var advertisedMachineData = MachineDataCharacteristics
            .Where(machineData => characteristics.Any(characteristic => characteristic.Id == machineData.Id))
            .ToArray();
        if (advertisedMachineData.Length == 0)
        {
            return null;
        }

        var selectedMachineData = advertisedMachineData.FirstOrDefault(machineData => machineData.MachineType == FtmsMachineDataType.IndoorBike);
        if (selectedMachineData == default)
        {
            selectedMachineData = advertisedMachineData[0];
        }

        var characteristic = characteristics.First(characteristic => characteristic.Id == selectedMachineData.Id);
        return (characteristic, selectedMachineData.MachineType, advertisedMachineData.Select(machineData => machineData.MachineType).ToArray());
    }

    private static async ValueTask<FitnessMachineFeature?> ReadFeaturesAsync(IService service)
    {
        try
        {
            var characteristic = await service.GetCharacteristicAsync(FitnessMachineFeatureId).ConfigureAwait(false);
            if (characteristic is not { CanRead: true })
            {
                return null;
            }

            await characteristic.ReadAsync().ConfigureAwait(false);
            return FitnessMachineFeature.TryDecode(characteristic.Value, out var features) == FtmsDecodeStatus.Success
                ? features
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static async ValueTask<FtmsSupportedRanges> ReadSupportedRangesAsync(IService service, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new FtmsSupportedRanges(
            await ReadRangeAsync<FtmsUInt16Range>(service, SupportedSpeedRangeId, FtmsUInt16Range.TryDecode).ConfigureAwait(false),
            await ReadRangeAsync<FtmsInt16Range>(service, SupportedInclinationRangeId, FtmsInt16Range.TryDecode).ConfigureAwait(false),
            await ReadRangeAsync<FtmsInt16Range>(service, SupportedResistanceLevelRangeId, FtmsInt16Range.TryDecode).ConfigureAwait(false),
            await ReadRangeAsync<FtmsUInt16Range>(service, SupportedPowerRangeId, FtmsUInt16Range.TryDecode).ConfigureAwait(false),
            await ReadRangeAsync<FtmsByteRange>(service, SupportedHeartRateRangeId, FtmsByteRange.TryDecode).ConfigureAwait(false));
    }

    private delegate FtmsDecodeStatus RangeDecoder<TRange>(ReadOnlySpan<byte> source, out TRange value, FtmsValidationMode validationMode);

    private static async ValueTask<TRange?> ReadRangeAsync<TRange>(IService service, Guid characteristicId, RangeDecoder<TRange> decoder)
        where TRange : struct
    {
        try
        {
            var characteristic = await service.GetCharacteristicAsync(characteristicId).ConfigureAwait(false);
            if (characteristic is null)
            {
                return null;
            }

            var (data, _) = await characteristic.ReadAsync().ConfigureAwait(false);
            return decoder(data, out var range, FtmsValidationMode.Strict) == FtmsDecodeStatus.Success
                ? range
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static readonly (Guid Id, FtmsMachineDataType MachineType)[] MachineDataCharacteristics =
    [
        (BluetoothUuid.FromAssignedNumber(0x2ACD), FtmsMachineDataType.Treadmill),
        (BluetoothUuid.FromAssignedNumber(0x2ACE), FtmsMachineDataType.CrossTrainer),
        (BluetoothUuid.FromAssignedNumber(0x2ACF), FtmsMachineDataType.StepClimber),
        (BluetoothUuid.FromAssignedNumber(0x2AD0), FtmsMachineDataType.StairClimber),
        (BluetoothUuid.FromAssignedNumber(0x2AD1), FtmsMachineDataType.Rower),
        (BluetoothUuid.FromAssignedNumber(0x2AD2), FtmsMachineDataType.IndoorBike),
    ];
}