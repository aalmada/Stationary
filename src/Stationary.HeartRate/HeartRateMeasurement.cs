using System.Buffers.Binary;

namespace Stationary.HeartRate;

public readonly record struct HeartRateMeasurement(
    ushort BeatsPerMinute,
    bool UsesSixteenBitValue,
    HeartRateSensorContactStatus SensorContactStatus,
    ushort? EnergyExpendedKilojoules,
    int RrIntervalCount)
{
    public static HeartRateDecodeStatus TryDecode(
        ReadOnlySpan<byte> source,
        out HeartRateMeasurement measurement,
        out ReadOnlySpan<byte> rrIntervalData,
        HeartRateValidationMode validationMode = HeartRateValidationMode.Compatible)
    {
        measurement = default;
        rrIntervalData = default;
        if (source.Length < 2)
        {
            return HeartRateDecodeStatus.InsufficientData;
        }

        var flags = source[0];
        var usesSixteenBitValue = (flags & 0b0000_0001) != 0;
        var offset = 1;
        ushort beatsPerMinute;
        if (usesSixteenBitValue)
        {
            if (source.Length - offset < sizeof(ushort))
            {
                return HeartRateDecodeStatus.InsufficientData;
            }

            beatsPerMinute = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
            offset += sizeof(ushort);
        }
        else
        {
            beatsPerMinute = source[offset++];
        }

        var contactStatus = GetSensorContactStatus(flags);
        ushort? energyExpendedKilojoules = null;
        if ((flags & 0b0000_1000) != 0)
        {
            if (source.Length - offset < sizeof(ushort))
            {
                return HeartRateDecodeStatus.InsufficientData;
            }

            energyExpendedKilojoules = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
            offset += sizeof(ushort);
        }

        if ((flags & 0b0001_0000) != 0)
        {
            rrIntervalData = source[offset..];
            if (rrIntervalData.Length % sizeof(ushort) != 0)
            {
                return HeartRateDecodeStatus.InsufficientData;
            }

            offset = source.Length;
        }
        else if (validationMode == HeartRateValidationMode.Strict && source.Length != offset)
        {
            return HeartRateDecodeStatus.TrailingData;
        }

        measurement = new(
            beatsPerMinute,
            usesSixteenBitValue,
            contactStatus,
            energyExpendedKilojoules,
            rrIntervalData.Length / sizeof(ushort));
        return HeartRateDecodeStatus.Success;
    }

    public static bool TryGetRrInterval(ReadOnlySpan<byte> rrIntervalData, int index, out ushort value)
    {
        value = 0;
        if ((uint)index >= (uint)(rrIntervalData.Length / sizeof(ushort)))
        {
            return false;
        }

        var offset = index * sizeof(ushort);
        if (rrIntervalData.Length - offset < sizeof(ushort))
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt16LittleEndian(rrIntervalData[offset..]);
        return true;
    }

    public static double GetRrIntervalSeconds(ushort value) => value / 1024d;

    private static HeartRateSensorContactStatus GetSensorContactStatus(byte flags) => (flags & 0b0000_0100) == 0
        ? HeartRateSensorContactStatus.NotSupported
        : (flags & 0b0000_0010) == 0
            ? HeartRateSensorContactStatus.NotDetected
            : HeartRateSensorContactStatus.Detected;
}