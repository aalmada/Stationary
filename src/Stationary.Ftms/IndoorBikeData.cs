using System.Buffers.Binary;

namespace Stationary.Ftms;

public readonly record struct IndoorBikeData(
    bool MoreData,
    ushort? InstantaneousSpeed,
    ushort? AverageSpeed,
    ushort? InstantaneousCadence,
    ushort? AverageCadence,
    uint? TotalDistance,
    short? ResistanceLevel,
    short? InstantaneousPower,
    short? AveragePower,
    ushort? TotalEnergy,
    ushort? EnergyPerHour,
    byte? EnergyPerMinute,
    byte? HeartRate,
    byte? MetabolicEquivalent,
    ushort? ElapsedTime,
    ushort? RemainingTime)
{
    private const ushort MoreDataMask = 1 << 0;
    private const ushort AverageSpeedMask = 1 << 1;
    private const ushort InstantaneousCadenceMask = 1 << 2;
    private const ushort AverageCadenceMask = 1 << 3;
    private const ushort TotalDistanceMask = 1 << 4;
    private const ushort ResistanceLevelMask = 1 << 5;
    private const ushort InstantaneousPowerMask = 1 << 6;
    private const ushort AveragePowerMask = 1 << 7;
    private const ushort ExpendedEnergyMask = 1 << 8;
    private const ushort HeartRateMask = 1 << 9;
    private const ushort MetabolicEquivalentMask = 1 << 10;
    private const ushort ElapsedTimeMask = 1 << 11;
    private const ushort RemainingTimeMask = 1 << 12;
    private const ushort DefinedFlagsMask = 0x1FFF;

    public double? InstantaneousSpeedKilometersPerHour => InstantaneousSpeed is ushort value ? value / 100d : null;
    public double? InstantaneousCadenceRpm => InstantaneousCadence is ushort value ? value / 2d : null;
    public double? MetabolicEquivalentValue => MetabolicEquivalent is byte value ? value / 10d : null;

    public int GetEncodedLength()
    {
        var length = sizeof(ushort);
        if (!MoreData) length += sizeof(ushort);
        if (AverageSpeed.HasValue) length += sizeof(ushort);
        if (InstantaneousCadence.HasValue) length += sizeof(ushort);
        if (AverageCadence.HasValue) length += sizeof(ushort);
        if (TotalDistance.HasValue) length += 3;
        if (ResistanceLevel.HasValue) length += sizeof(short);
        if (InstantaneousPower.HasValue) length += sizeof(short);
        if (AveragePower.HasValue) length += sizeof(short);
        if (TotalEnergy.HasValue || EnergyPerHour.HasValue || EnergyPerMinute.HasValue) length += 5;
        if (HeartRate.HasValue) length += sizeof(byte);
        if (MetabolicEquivalent.HasValue) length += sizeof(byte);
        if (ElapsedTime.HasValue) length += sizeof(ushort);
        if (RemainingTime.HasValue) length += sizeof(ushort);
        return length;
    }

    public static FtmsDecodeStatus TryDecode(ReadOnlySpan<byte> source, out IndoorBikeData value, FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        value = default;
        if (source.Length < sizeof(ushort)) return FtmsDecodeStatus.InsufficientData;

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(source);
        if (validationMode == FtmsValidationMode.Strict && (flags & ~DefinedFlagsMask) != 0) return FtmsDecodeStatus.InvalidFlags;
        var offset = sizeof(ushort);
        var moreData = (flags & MoreDataMask) != 0;

        if (!TryReadOptional(!moreData, source, ref offset, out ushort? instantaneousSpeed) ||
            !TryReadOptional((flags & AverageSpeedMask) != 0, source, ref offset, out ushort? averageSpeed) ||
            !TryReadOptional((flags & InstantaneousCadenceMask) != 0, source, ref offset, out ushort? instantaneousCadence) ||
            !TryReadOptional((flags & AverageCadenceMask) != 0, source, ref offset, out ushort? averageCadence) ||
            !TryReadUInt24Optional((flags & TotalDistanceMask) != 0, source, ref offset, out uint? totalDistance) ||
            !TryReadOptional((flags & ResistanceLevelMask) != 0, source, ref offset, out short? resistanceLevel) ||
            !TryReadOptional((flags & InstantaneousPowerMask) != 0, source, ref offset, out short? instantaneousPower) ||
            !TryReadOptional((flags & AveragePowerMask) != 0, source, ref offset, out short? averagePower) ||
            !TryReadEnergyOptional((flags & ExpendedEnergyMask) != 0, source, ref offset, out ushort? totalEnergy, out ushort? energyPerHour, out byte? energyPerMinute) ||
            !TryReadOptional((flags & HeartRateMask) != 0, source, ref offset, out byte? heartRate) ||
            !TryReadOptional((flags & MetabolicEquivalentMask) != 0, source, ref offset, out byte? metabolicEquivalent) ||
            !TryReadOptional((flags & ElapsedTimeMask) != 0, source, ref offset, out ushort? elapsedTime) ||
            !TryReadOptional((flags & RemainingTimeMask) != 0, source, ref offset, out ushort? remainingTime))
        {
            return FtmsDecodeStatus.InsufficientData;
        }

        if (validationMode == FtmsValidationMode.Strict && offset != source.Length) return FtmsDecodeStatus.TrailingData;

        value = new IndoorBikeData(moreData, instantaneousSpeed, averageSpeed, instantaneousCadence, averageCadence, totalDistance, resistanceLevel, instantaneousPower, averagePower, totalEnergy, energyPerHour, energyPerMinute, heartRate, metabolicEquivalent, elapsedTime, remainingTime);
        return FtmsDecodeStatus.Success;
    }

    public bool TryEncode(Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if ((!MoreData && !InstantaneousSpeed.HasValue) || destination.Length < GetEncodedLength()) return false;

        var flags = GetFlags();
        BinaryPrimitives.WriteUInt16LittleEndian(destination, flags);
        var offset = sizeof(ushort);
        if (!MoreData) WriteUInt16(destination, ref offset, InstantaneousSpeed!.Value);
        if (AverageSpeed.HasValue) WriteUInt16(destination, ref offset, AverageSpeed.Value);
        if (InstantaneousCadence.HasValue) WriteUInt16(destination, ref offset, InstantaneousCadence.Value);
        if (AverageCadence.HasValue) WriteUInt16(destination, ref offset, AverageCadence.Value);
        if (TotalDistance.HasValue) WriteUInt24(destination, ref offset, TotalDistance.Value);
        if (ResistanceLevel.HasValue) WriteInt16(destination, ref offset, ResistanceLevel.Value);
        if (InstantaneousPower.HasValue) WriteInt16(destination, ref offset, InstantaneousPower.Value);
        if (AveragePower.HasValue) WriteInt16(destination, ref offset, AveragePower.Value);
        if (TotalEnergy.HasValue || EnergyPerHour.HasValue || EnergyPerMinute.HasValue)
        {
            WriteUInt16(destination, ref offset, TotalEnergy ?? ushort.MaxValue);
            WriteUInt16(destination, ref offset, EnergyPerHour ?? ushort.MaxValue);
            destination[offset++] = EnergyPerMinute ?? byte.MaxValue;
        }
        if (HeartRate.HasValue) destination[offset++] = HeartRate.Value;
        if (MetabolicEquivalent.HasValue) destination[offset++] = MetabolicEquivalent.Value;
        if (ElapsedTime.HasValue) WriteUInt16(destination, ref offset, ElapsedTime.Value);
        if (RemainingTime.HasValue) WriteUInt16(destination, ref offset, RemainingTime.Value);
        bytesWritten = offset;
        return true;
    }

    private ushort GetFlags()
    {
        var flags = MoreData ? MoreDataMask : (ushort)0;
        if (AverageSpeed.HasValue) flags |= AverageSpeedMask;
        if (InstantaneousCadence.HasValue) flags |= InstantaneousCadenceMask;
        if (AverageCadence.HasValue) flags |= AverageCadenceMask;
        if (TotalDistance.HasValue) flags |= TotalDistanceMask;
        if (ResistanceLevel.HasValue) flags |= ResistanceLevelMask;
        if (InstantaneousPower.HasValue) flags |= InstantaneousPowerMask;
        if (AveragePower.HasValue) flags |= AveragePowerMask;
        if (TotalEnergy.HasValue || EnergyPerHour.HasValue || EnergyPerMinute.HasValue) flags |= ExpendedEnergyMask;
        if (HeartRate.HasValue) flags |= HeartRateMask;
        if (MetabolicEquivalent.HasValue) flags |= MetabolicEquivalentMask;
        if (ElapsedTime.HasValue) flags |= ElapsedTimeMask;
        if (RemainingTime.HasValue) flags |= RemainingTimeMask;
        return flags;
    }

    private static bool TryReadOptional(bool present, ReadOnlySpan<byte> source, ref int offset, out ushort? value)
    {
        value = null;
        if (!present) return true;
        if (source.Length - offset < sizeof(ushort)) return false;
        value = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
        offset += sizeof(ushort);
        return true;
    }

    private static bool TryReadOptional(bool present, ReadOnlySpan<byte> source, ref int offset, out short? value)
    {
        value = null;
        if (!present) return true;
        if (source.Length - offset < sizeof(short)) return false;
        value = BinaryPrimitives.ReadInt16LittleEndian(source[offset..]);
        offset += sizeof(short);
        return true;
    }

    private static bool TryReadOptional(bool present, ReadOnlySpan<byte> source, ref int offset, out byte? value)
    {
        value = null;
        if (!present) return true;
        if (source.Length == offset) return false;
        value = source[offset++];
        return true;
    }

    private static bool TryReadUInt24Optional(bool present, ReadOnlySpan<byte> source, ref int offset, out uint? value)
    {
        value = null;
        if (!present) return true;
        if (source.Length - offset < 3) return false;
        value = (uint)(source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16));
        offset += 3;
        return true;
    }

    private static bool TryReadEnergyOptional(bool present, ReadOnlySpan<byte> source, ref int offset, out ushort? total, out ushort? perHour, out byte? perMinute)
    {
        total = null;
        perHour = null;
        perMinute = null;
        if (!present) return true;
        if (source.Length - offset < 5) return false;
        total = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
        perHour = BinaryPrimitives.ReadUInt16LittleEndian(source[(offset + 2)..]);
        perMinute = source[offset + 4];
        offset += 5;
        return true;
    }

    private static void WriteUInt16(Span<byte> destination, ref int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], value);
        offset += sizeof(ushort);
    }

    private static void WriteInt16(Span<byte> destination, ref int offset, short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(destination[offset..], value);
        offset += sizeof(short);
    }

    private static void WriteUInt24(Span<byte> destination, ref int offset, uint value)
    {
        destination[offset++] = (byte)value;
        destination[offset++] = (byte)(value >> 8);
        destination[offset++] = (byte)(value >> 16);
    }
}