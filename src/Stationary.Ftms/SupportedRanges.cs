using System.Buffers.Binary;

namespace Stationary.Ftms;

public readonly record struct FtmsUInt16Range(ushort Minimum, ushort Maximum, ushort Increment)
{
    public const int EncodedLength = 6;

    public static FtmsDecodeStatus TryDecode(ReadOnlySpan<byte> source, out FtmsUInt16Range value, FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        value = default;
        if (source.Length < EncodedLength) return FtmsDecodeStatus.InsufficientData;
        if (validationMode == FtmsValidationMode.Strict && source.Length != EncodedLength) return FtmsDecodeStatus.TrailingData;
        value = new FtmsUInt16Range(BinaryPrimitives.ReadUInt16LittleEndian(source), BinaryPrimitives.ReadUInt16LittleEndian(source[2..]), BinaryPrimitives.ReadUInt16LittleEndian(source[4..]));
        return FtmsDecodeStatus.Success;
    }
}

public readonly record struct FtmsInt16Range(short Minimum, short Maximum, short Increment)
{
    public const int EncodedLength = 6;

    public static FtmsDecodeStatus TryDecode(ReadOnlySpan<byte> source, out FtmsInt16Range value, FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        value = default;
        if (source.Length < EncodedLength) return FtmsDecodeStatus.InsufficientData;
        if (validationMode == FtmsValidationMode.Strict && source.Length != EncodedLength) return FtmsDecodeStatus.TrailingData;
        value = new FtmsInt16Range(BinaryPrimitives.ReadInt16LittleEndian(source), BinaryPrimitives.ReadInt16LittleEndian(source[2..]), BinaryPrimitives.ReadInt16LittleEndian(source[4..]));
        return FtmsDecodeStatus.Success;
    }
}

public readonly record struct FtmsByteRange(byte Minimum, byte Maximum, byte Increment)
{
    public const int EncodedLength = 3;

    public static FtmsDecodeStatus TryDecode(ReadOnlySpan<byte> source, out FtmsByteRange value, FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        value = default;
        if (source.Length < EncodedLength) return FtmsDecodeStatus.InsufficientData;
        if (validationMode == FtmsValidationMode.Strict && source.Length != EncodedLength) return FtmsDecodeStatus.TrailingData;
        value = new FtmsByteRange(source[0], source[1], source[2]);
        return FtmsDecodeStatus.Success;
    }
}