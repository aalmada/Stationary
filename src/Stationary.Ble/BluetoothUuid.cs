using System.Buffers.Binary;

namespace Stationary.Ble;

public static class BluetoothUuid
{
    public static Guid FromAssignedNumber(ushort assignedNumber)
    {
        Span<byte> value =
        [
            0, 0, 0, 0, 0, 0, 0x10, 0,
            0x80, 0, 0, 0x80, 0x5F, 0x9B, 0x34, 0xFB,
        ];
        BinaryPrimitives.WriteUInt16BigEndian(value.Slice(2, sizeof(ushort)), assignedNumber);
        return new Guid(value, bigEndian: true);
    }
}