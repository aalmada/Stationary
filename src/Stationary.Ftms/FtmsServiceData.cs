using System.Buffers.Binary;

namespace Stationary.Ftms;

public readonly record struct FtmsServiceData(bool IsMachineAvailable, ushort MachineTypes)
{
    public const ushort ServiceUuid = 0x1826;
    public const byte ServiceDataAdType = 0x16;
    public const int EncodedLength = 6;
    public const ushort DefinedMachineTypeMask = 0x003F;

    public static FtmsDecodeStatus TryDecode(ReadOnlySpan<byte> source, out FtmsServiceData value, FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        value = default;
        if (source.Length < EncodedLength) return FtmsDecodeStatus.InsufficientData;
        if (source[0] != ServiceDataAdType || BinaryPrimitives.ReadUInt16LittleEndian(source[1..]) != ServiceUuid) return FtmsDecodeStatus.InvalidFlags;
        var flags = source[3];
        var machineTypes = BinaryPrimitives.ReadUInt16LittleEndian(source[4..]);
        if (validationMode == FtmsValidationMode.Strict && (source.Length != EncodedLength || (flags & 0xFE) != 0 || (machineTypes & ~DefinedMachineTypeMask) != 0)) return FtmsDecodeStatus.InvalidFlags;
        value = new FtmsServiceData((flags & 1) != 0, machineTypes);
        return FtmsDecodeStatus.Success;
    }

    public bool TryEncode(Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (destination.Length < EncodedLength) return false;
        destination[0] = ServiceDataAdType;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[1..], ServiceUuid);
        destination[3] = IsMachineAvailable ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)(MachineTypes & DefinedMachineTypeMask));
        bytesWritten = EncodedLength;
        return true;
    }
}