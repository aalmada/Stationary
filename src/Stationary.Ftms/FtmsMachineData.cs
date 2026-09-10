using System.Buffers.Binary;

namespace Stationary.Ftms;

public enum FtmsMachineDataType : byte
{
    Treadmill,
    CrossTrainer,
    StepClimber,
    StairClimber,
    Rower,
    IndoorBike,
}

public static class FtmsMachineData
{
    public static FtmsDecodeStatus TryValidate(FtmsMachineDataType type, ReadOnlySpan<byte> source, FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        if (source.Length < sizeof(ushort)) return FtmsDecodeStatus.InsufficientData;
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(source);
        var offset = sizeof(ushort);
        var result = type switch
        {
            FtmsMachineDataType.Treadmill => TryConsumeTreadmill(flags, source, ref offset, validationMode),
            FtmsMachineDataType.CrossTrainer => TryConsumeCrossTrainer(flags, source, ref offset, validationMode),
            FtmsMachineDataType.StepClimber => TryConsumeStepClimber(flags, source, ref offset, validationMode),
            FtmsMachineDataType.StairClimber => TryConsumeStairClimber(flags, source, ref offset, validationMode),
            FtmsMachineDataType.Rower => TryConsumeRower(flags, source, ref offset, validationMode),
            FtmsMachineDataType.IndoorBike => TryConsumeIndoorBike(flags, source, ref offset, validationMode),
            _ => FtmsDecodeStatus.InvalidFlags,
        };
        if (result != FtmsDecodeStatus.Success) return result;
        return validationMode == FtmsValidationMode.Strict && offset != source.Length ? FtmsDecodeStatus.TrailingData : FtmsDecodeStatus.Success;
    }

    private static FtmsDecodeStatus TryConsumeTreadmill(ushort flags, ReadOnlySpan<byte> source, ref int offset, FtmsValidationMode validationMode)
        => TryConsume(flags, 0x1FFF, source, ref offset, validationMode, [2, 2, 3, 4, 4, 2, 2, 5, 1, 1, 2, 2, 4]);

    private static FtmsDecodeStatus TryConsumeCrossTrainer(ushort flags, ReadOnlySpan<byte> source, ref int offset, FtmsValidationMode validationMode)
        => TryConsume(flags, ushort.MaxValue, source, ref offset, validationMode, [2, 2, 3, 4, 2, 4, 4, 1, 2, 2, 5, 1, 1, 2, 2]);

    private static FtmsDecodeStatus TryConsumeStepClimber(ushort flags, ReadOnlySpan<byte> source, ref int offset, FtmsValidationMode validationMode)
        => TryConsume(flags, 0x01FF, source, ref offset, validationMode, [4, 2, 2, 2, 5, 1, 1, 2, 2]);

    private static FtmsDecodeStatus TryConsumeStairClimber(ushort flags, ReadOnlySpan<byte> source, ref int offset, FtmsValidationMode validationMode)
        => TryConsume(flags, 0x03FF, source, ref offset, validationMode, [2, 2, 2, 2, 2, 5, 1, 1, 2, 2]);

    private static FtmsDecodeStatus TryConsumeRower(ushort flags, ReadOnlySpan<byte> source, ref int offset, FtmsValidationMode validationMode)
        => TryConsume(flags, 0x1FFF, source, ref offset, validationMode, [3, 1, 3, 2, 2, 2, 2, 1, 5, 1, 1, 2, 2]);

    private static FtmsDecodeStatus TryConsumeIndoorBike(ushort flags, ReadOnlySpan<byte> source, ref int offset, FtmsValidationMode validationMode)
        => TryConsume(flags, 0x1FFF, source, ref offset, validationMode, [2, 2, 2, 2, 3, 2, 2, 2, 5, 1, 1, 2, 2]);

    private static FtmsDecodeStatus TryConsume(ushort flags, ushort definedMask, ReadOnlySpan<byte> source, ref int offset, FtmsValidationMode validationMode, ReadOnlySpan<byte> fieldLengths)
    {
        if (validationMode == FtmsValidationMode.Strict && (flags & ~definedMask) != 0) return FtmsDecodeStatus.InvalidFlags;
        if ((flags & 1) == 0)
        {
            var firstLength = fieldLengths[0];
            if (source.Length - offset < firstLength) return FtmsDecodeStatus.InsufficientData;
            offset += firstLength;
        }
        for (var bit = 1; bit < fieldLengths.Length; bit++)
        {
            if ((flags & (1 << bit)) == 0) continue;
            var length = fieldLengths[bit];
            if (source.Length - offset < length) return FtmsDecodeStatus.InsufficientData;
            offset += length;
        }
        return FtmsDecodeStatus.Success;
    }
}