using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

using Stationary.Ftms.Dashboard.Core;
using Stationary.HeartRate;

namespace Stationary.Ftms.Dashboard.Services;

public sealed class PluginBleHeartRateSession : IHeartRateSession
{
    private readonly IAdapter adapter;
    private readonly IDevice device;
    private readonly ICharacteristic measurementCharacteristic;
    private readonly EventHandler<CharacteristicUpdatedEventArgs> valueUpdated;
    private readonly EventHandler<DeviceErrorEventArgs> connectionLost;
    private int disposed;
    private long measurementsReceived;

    public PluginBleHeartRateSession(
        IAdapter adapter,
        IDevice device,
        ICharacteristic measurementCharacteristic,
        EventHandler<CharacteristicUpdatedEventArgs> valueUpdated,
        LatestHeartRateBuffer telemetry)
    {
        this.adapter = adapter;
        this.device = device;
        this.measurementCharacteristic = measurementCharacteristic;
        this.valueUpdated = valueUpdated;
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

    public long MeasurementsReceived => Interlocked.Read(ref measurementsReceived);

    public LatestHeartRateBuffer Telemetry { get; }

    public event EventHandler? MeasurementReceived;

    public event EventHandler? ConnectionLost;

    public void RecordMeasurement(ReadOnlySpan<byte> payload, DateTimeOffset capturedAt)
    {
        if (HeartRateMeasurement.TryDecode(payload, out var measurement, out _) != HeartRateDecodeStatus.Success
            || !Telemetry.TryPublish(new(capturedAt, measurement.BeatsPerMinute)))
        {
            return;
        }

        Interlocked.Increment(ref measurementsReceived);
        MeasurementReceived?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        adapter.DeviceConnectionLost -= connectionLost;
        measurementCharacteristic.ValueUpdated -= valueUpdated;
        try
        {
            await measurementCharacteristic.StopUpdatesAsync().ConfigureAwait(false);
        }
        finally
        {
            Telemetry.Complete();
            await adapter.DisconnectDeviceAsync(device).ConfigureAwait(false);
        }
    }
}