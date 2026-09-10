using Stationary.Ble;

namespace Stationary.HeartRate;

public static class HeartRateService
{
    public static Guid Id { get; } = BluetoothUuid.FromAssignedNumber(0x180D);

    public static Guid MeasurementCharacteristicId { get; } = BluetoothUuid.FromAssignedNumber(0x2A37);

    public static Guid BodySensorLocationCharacteristicId { get; } = BluetoothUuid.FromAssignedNumber(0x2A38);

    public static Guid ControlPointCharacteristicId { get; } = BluetoothUuid.FromAssignedNumber(0x2A39);
}