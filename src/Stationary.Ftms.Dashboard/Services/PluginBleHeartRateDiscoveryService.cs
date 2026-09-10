using System.Collections.Concurrent;

using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

using Stationary.Ftms.Dashboard.Core;
using Stationary.HeartRate;

namespace Stationary.Ftms.Dashboard.Services;

public sealed class PluginBleHeartRateDiscoveryService : IHeartRateDiscoveryService
{
    private readonly IAdapter adapter = CrossBluetoothLE.Current.Adapter;
    private readonly ConcurrentDictionary<Guid, IDevice> discoveredDevices = [];

    public async ValueTask DiscoverAsync(Action<HeartRateDiscoveredDevice> deviceDiscovered, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceDiscovered);

        EventHandler<DeviceEventArgs>? discovered = (_, eventArgs) =>
        {
            if (!discoveredDevices.TryAdd(eventArgs.Device.Id, eventArgs.Device))
            {
                discoveredDevices[eventArgs.Device.Id] = eventArgs.Device;
                return;
            }

            deviceDiscovered(new HeartRateDiscoveredDevice(
                eventArgs.Device.Id.ToString(),
                eventArgs.Device.Name ?? "Unnamed heart-rate sensor"));
        };

        discoveredDevices.Clear();
        adapter.DeviceDiscovered += discovered;
        try
        {
            adapter.ScanMode = ScanMode.LowLatency;
            adapter.ScanTimeout = 20_000;
            await adapter.StartScanningForDevicesAsync([HeartRateService.Id], _ => true, false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            adapter.DeviceDiscovered -= discovered;
            await adapter.StopScanningForDevicesAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<IHeartRateSession?> ConnectAsync(HeartRateDiscoveredDevice device, CancellationToken cancellationToken)
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

    private async ValueTask<IHeartRateSession?> TryConnectAsync(IDevice device, CancellationToken cancellationToken)
    {
        ICharacteristic? measurementCharacteristic = null;
        EventHandler<CharacteristicUpdatedEventArgs>? valueUpdated = null;
        try
        {
            await adapter.ConnectToDeviceAsync(device, cancellationToken: cancellationToken).ConfigureAwait(false);
            var service = await device.GetServiceAsync(HeartRateService.Id).ConfigureAwait(false);
            var candidate = await service.GetCharacteristicAsync(HeartRateService.MeasurementCharacteristicId).ConfigureAwait(false);
            if (candidate is not { CanUpdate: true })
            {
                await DisconnectQuietlyAsync(device).ConfigureAwait(false);
                return null;
            }

            var telemetry = new LatestHeartRateBuffer();
            PluginBleHeartRateSession? session = null;
            measurementCharacteristic = candidate;
            valueUpdated = (_, eventArgs) => session?.RecordMeasurement(eventArgs.Characteristic.Value, DateTimeOffset.UtcNow);
            measurementCharacteristic.ValueUpdated += valueUpdated;
            await measurementCharacteristic.StartUpdatesAsync(cancellationToken).ConfigureAwait(false);

            session = new PluginBleHeartRateSession(adapter, device, measurementCharacteristic, valueUpdated, telemetry);
            return session;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CleanupFailedConnectionAsync(device, measurementCharacteristic, valueUpdated).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await CleanupFailedConnectionAsync(device, measurementCharacteristic, valueUpdated).ConfigureAwait(false);
            return null;
        }
    }

    private async ValueTask CleanupFailedConnectionAsync(
        IDevice device,
        ICharacteristic? measurementCharacteristic,
        EventHandler<CharacteristicUpdatedEventArgs>? valueUpdated)
    {
        if (measurementCharacteristic is not null && valueUpdated is not null)
        {
            measurementCharacteristic.ValueUpdated -= valueUpdated;
        }

        if (measurementCharacteristic is not null)
        {
            try
            {
                await measurementCharacteristic.StopUpdatesAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        await DisconnectQuietlyAsync(device).ConfigureAwait(false);
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
}