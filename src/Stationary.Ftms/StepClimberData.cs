using System.Buffers.Binary;

namespace Stationary.Ftms;

public readonly record struct StepClimberData(
    bool MoreData, ushort? Floors, ushort? StepCount, ushort? StepPerMinute, ushort? AverageStepRate,
    ushort? PositiveElevationGain, ushort? TotalEnergy, ushort? EnergyPerHour, byte? EnergyPerMinute,
    byte? HeartRate, byte? MetabolicEquivalent, ushort? ElapsedTime, ushort? RemainingTime)
{
    private const ushort DefinedFlagsMask = 0x01FF;

    public int GetEncodedLength() => 2 + (!MoreData ? 4 : 0) + (StepPerMinute.HasValue ? 2 : 0) +
        (AverageStepRate.HasValue ? 2 : 0) + (PositiveElevationGain.HasValue ? 2 : 0) +
        (HasEnergy ? 5 : 0) + (HeartRate.HasValue ? 1 : 0) + (MetabolicEquivalent.HasValue ? 1 : 0) +
        (ElapsedTime.HasValue ? 2 : 0) + (RemainingTime.HasValue ? 2 : 0);

    public static FtmsDecodeStatus TryDecode(ReadOnlySpan<byte> source, out StepClimberData value, FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        value = default;
        if (source.Length < 2) return FtmsDecodeStatus.InsufficientData;
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(source);
        if (validationMode == FtmsValidationMode.Strict && (flags & ~DefinedFlagsMask) != 0) return FtmsDecodeStatus.InvalidFlags;
        var reader = new FtmsFieldReader(source[2..]);
        var moreData = (flags & 1) != 0;
        ushort? floors = null, stepCount = null, stepPerMinute = null, averageStepRate = null, positiveElevationGain = null, totalEnergy = null, energyPerHour = null, elapsedTime = null, remainingTime = null;
        byte? energyPerMinute = null, heartRate = null, metabolicEquivalent = null;
        if (!moreData)
        {
            if (!reader.TryReadUInt16(out var floorValue) || !reader.TryReadUInt16(out var stepCountValue)) return FtmsDecodeStatus.InsufficientData;
            floors = floorValue;
            stepCount = stepCountValue;
        }
        if (!ReadOptional((flags & 2) != 0, ref reader, out stepPerMinute) || !ReadOptional((flags & 4) != 0, ref reader, out averageStepRate) ||
            !ReadOptional((flags & 8) != 0, ref reader, out positiveElevationGain) || !ReadEnergy((flags & 16) != 0, ref reader, out totalEnergy, out energyPerHour, out energyPerMinute) ||
            !ReadOptional((flags & 32) != 0, ref reader, out heartRate) || !ReadOptional((flags & 64) != 0, ref reader, out metabolicEquivalent) ||
            !ReadOptional((flags & 128) != 0, ref reader, out elapsedTime) || !ReadOptional((flags & 256) != 0, ref reader, out remainingTime)) return FtmsDecodeStatus.InsufficientData;
        if (validationMode == FtmsValidationMode.Strict && reader.Consumed != source.Length - 2) return FtmsDecodeStatus.TrailingData;
        value = new(moreData, floors, stepCount, stepPerMinute, averageStepRate, positiveElevationGain, totalEnergy, energyPerHour, energyPerMinute, heartRate, metabolicEquivalent, elapsedTime, remainingTime);
        return FtmsDecodeStatus.Success;
    }

    public bool TryEncode(Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if ((!MoreData && (!Floors.HasValue || !StepCount.HasValue)) || destination.Length < GetEncodedLength()) return false;
        var writer = new FtmsFieldWriter(destination);
        writer.WriteUInt16(GetFlags());
        if (!MoreData) { writer.WriteUInt16(Floors!.Value); writer.WriteUInt16(StepCount!.Value); }
        if (StepPerMinute is ushort stepPerMinute) writer.WriteUInt16(stepPerMinute);
        if (AverageStepRate is ushort averageStepRate) writer.WriteUInt16(averageStepRate);
        if (PositiveElevationGain is ushort elevation) writer.WriteUInt16(elevation);
        WriteEnergy(ref writer, HasEnergy, TotalEnergy, EnergyPerHour, EnergyPerMinute);
        if (HeartRate is byte heartRate) writer.WriteByte(heartRate);
        if (MetabolicEquivalent is byte metabolicEquivalent) writer.WriteByte(metabolicEquivalent);
        if (ElapsedTime is ushort elapsedTime) writer.WriteUInt16(elapsedTime);
        if (RemainingTime is ushort remainingTime) writer.WriteUInt16(remainingTime);
        bytesWritten = writer.Written;
        return true;
    }

    private bool HasEnergy => TotalEnergy.HasValue || EnergyPerHour.HasValue || EnergyPerMinute.HasValue;
    private ushort GetFlags() => (ushort)((MoreData ? 1 : 0) | (StepPerMinute.HasValue ? 2 : 0) | (AverageStepRate.HasValue ? 4 : 0) | (PositiveElevationGain.HasValue ? 8 : 0) | (HasEnergy ? 16 : 0) | (HeartRate.HasValue ? 32 : 0) | (MetabolicEquivalent.HasValue ? 64 : 0) | (ElapsedTime.HasValue ? 128 : 0) | (RemainingTime.HasValue ? 256 : 0));
    internal static bool ReadOptional(bool present, ref FtmsFieldReader reader, out ushort? value) { value = null; if (!present) return true; if (!reader.TryReadUInt16(out var raw)) return false; value = raw; return true; }
    internal static bool ReadOptional(bool present, ref FtmsFieldReader reader, out short? value) { value = null; if (!present) return true; if (!reader.TryReadInt16(out var raw)) return false; value = raw; return true; }
    internal static bool ReadOptional(bool present, ref FtmsFieldReader reader, out byte? value) { value = null; if (!present) return true; if (!reader.TryReadByte(out var raw)) return false; value = raw; return true; }
    internal static bool ReadOptionalUInt24(bool present, ref FtmsFieldReader reader, out uint? value) { value = null; if (!present) return true; if (!reader.TryReadUInt24(out var raw)) return false; value = raw; return true; }
    internal static bool ReadEnergy(bool present, ref FtmsFieldReader reader, out ushort? total, out ushort? perHour, out byte? perMinute) { total = null; perHour = null; perMinute = null; if (!present) return true; if (!reader.TryReadUInt16(out var totalRaw) || !reader.TryReadUInt16(out var perHourRaw) || !reader.TryReadByte(out var perMinuteRaw)) return false; total = totalRaw; perHour = perHourRaw; perMinute = perMinuteRaw; return true; }
    internal static void WriteEnergy(ref FtmsFieldWriter writer, bool present, ushort? total, ushort? perHour, byte? perMinute) { if (!present) return; writer.WriteUInt16(total ?? ushort.MaxValue); writer.WriteUInt16(perHour ?? ushort.MaxValue); writer.WriteByte(perMinute ?? byte.MaxValue); }
}