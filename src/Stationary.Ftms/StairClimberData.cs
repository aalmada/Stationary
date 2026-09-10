using System.Buffers.Binary;

namespace Stationary.Ftms;

public readonly record struct StairClimberData(
    bool MoreData, ushort? Floors, ushort? StepPerMinute, ushort? AverageStepRate, ushort? PositiveElevationGain,
    ushort? StrideCount, ushort? TotalEnergy, ushort? EnergyPerHour, byte? EnergyPerMinute, byte? HeartRate,
    byte? MetabolicEquivalent, ushort? ElapsedTime, ushort? RemainingTime)
{
    private const ushort DefinedFlagsMask = 0x03FF;
    public int GetEncodedLength() => 2 + (!MoreData ? 2 : 0) + (StepPerMinute.HasValue ? 2 : 0) + (AverageStepRate.HasValue ? 2 : 0) + (PositiveElevationGain.HasValue ? 2 : 0) + (StrideCount.HasValue ? 2 : 0) + (HasEnergy ? 5 : 0) + (HeartRate.HasValue ? 1 : 0) + (MetabolicEquivalent.HasValue ? 1 : 0) + (ElapsedTime.HasValue ? 2 : 0) + (RemainingTime.HasValue ? 2 : 0);
    public static FtmsDecodeStatus TryDecode(ReadOnlySpan<byte> source, out StairClimberData value, FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        value = default; if (source.Length < 2) return FtmsDecodeStatus.InsufficientData; var flags = BinaryPrimitives.ReadUInt16LittleEndian(source); if (validationMode == FtmsValidationMode.Strict && (flags & ~DefinedFlagsMask) != 0) return FtmsDecodeStatus.InvalidFlags;
        var reader = new FtmsFieldReader(source[2..]); var moreData = (flags & 1) != 0; ushort? floors = null, stepPerMinute = null, averageStepRate = null, positiveElevationGain = null, strideCount = null, totalEnergy = null, energyPerHour = null, elapsedTime = null, remainingTime = null; byte? energyPerMinute = null, heartRate = null, metabolicEquivalent = null;
        if (!moreData)
        {
            if (!reader.TryReadUInt16(out var floorValue)) return FtmsDecodeStatus.InsufficientData;
            floors = floorValue;
        }
        if (!StepClimberData.ReadOptional((flags & 2) != 0, ref reader, out stepPerMinute) || !StepClimberData.ReadOptional((flags & 4) != 0, ref reader, out averageStepRate) || !StepClimberData.ReadOptional((flags & 8) != 0, ref reader, out positiveElevationGain) || !StepClimberData.ReadOptional((flags & 16) != 0, ref reader, out strideCount) || !StepClimberData.ReadEnergy((flags & 32) != 0, ref reader, out totalEnergy, out energyPerHour, out energyPerMinute) || !StepClimberData.ReadOptional((flags & 64) != 0, ref reader, out heartRate) || !StepClimberData.ReadOptional((flags & 128) != 0, ref reader, out metabolicEquivalent) || !StepClimberData.ReadOptional((flags & 256) != 0, ref reader, out elapsedTime) || !StepClimberData.ReadOptional((flags & 512) != 0, ref reader, out remainingTime)) return FtmsDecodeStatus.InsufficientData;
        if (validationMode == FtmsValidationMode.Strict && reader.Consumed != source.Length - 2) return FtmsDecodeStatus.TrailingData; value = new(moreData, floors, stepPerMinute, averageStepRate, positiveElevationGain, strideCount, totalEnergy, energyPerHour, energyPerMinute, heartRate, metabolicEquivalent, elapsedTime, remainingTime); return FtmsDecodeStatus.Success;
    }
    public bool TryEncode(Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0; if ((!MoreData && !Floors.HasValue) || destination.Length < GetEncodedLength()) return false; var writer = new FtmsFieldWriter(destination); writer.WriteUInt16(GetFlags()); if (!MoreData) writer.WriteUInt16(Floors!.Value); if (StepPerMinute is ushort a) writer.WriteUInt16(a); if (AverageStepRate is ushort b) writer.WriteUInt16(b); if (PositiveElevationGain is ushort c) writer.WriteUInt16(c); if (StrideCount is ushort d) writer.WriteUInt16(d); StepClimberData.WriteEnergy(ref writer, HasEnergy, TotalEnergy, EnergyPerHour, EnergyPerMinute); if (HeartRate is byte e) writer.WriteByte(e); if (MetabolicEquivalent is byte f) writer.WriteByte(f); if (ElapsedTime is ushort g) writer.WriteUInt16(g); if (RemainingTime is ushort h) writer.WriteUInt16(h); bytesWritten = writer.Written; return true;
    }
    private bool HasEnergy => TotalEnergy.HasValue || EnergyPerHour.HasValue || EnergyPerMinute.HasValue;
    private ushort GetFlags() => (ushort)((MoreData ? 1 : 0) | (StepPerMinute.HasValue ? 2 : 0) | (AverageStepRate.HasValue ? 4 : 0) | (PositiveElevationGain.HasValue ? 8 : 0) | (StrideCount.HasValue ? 16 : 0) | (HasEnergy ? 32 : 0) | (HeartRate.HasValue ? 64 : 0) | (MetabolicEquivalent.HasValue ? 128 : 0) | (ElapsedTime.HasValue ? 256 : 0) | (RemainingTime.HasValue ? 512 : 0));
}