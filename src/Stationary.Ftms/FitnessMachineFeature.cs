using System.Buffers.Binary;

namespace Stationary.Ftms;

public readonly record struct FitnessMachineFeature(uint MachineFeatures, uint TargetSettingFeatures)
{
    public const int EncodedLength = 8;
    public const uint DefinedMachineFeatureMask = 0x0001_FFFF;
    public const uint DefinedTargetSettingFeatureMask = 0x0001_FFFF;

    public bool SupportsAverageSpeed => (MachineFeatures & (1u << 0)) != 0;
    public bool SupportsCadence => (MachineFeatures & (1u << 1)) != 0;
    public bool SupportsTotalDistance => (MachineFeatures & (1u << 2)) != 0;
    public bool SupportsInclination => (MachineFeatures & (1u << 3)) != 0;
    public bool SupportsElevationGain => (MachineFeatures & (1u << 4)) != 0;
    public bool SupportsPace => (MachineFeatures & (1u << 5)) != 0;
    public bool SupportsStepCount => (MachineFeatures & (1u << 6)) != 0;
    public bool SupportsResistanceLevel => (MachineFeatures & (1u << 7)) != 0;
    public bool SupportsStrideCount => (MachineFeatures & (1u << 8)) != 0;
    public bool SupportsExpendedEnergy => (MachineFeatures & (1u << 9)) != 0;
    public bool SupportsHeartRateMeasurement => (MachineFeatures & (1u << 10)) != 0;
    public bool SupportsMetabolicEquivalent => (MachineFeatures & (1u << 11)) != 0;
    public bool SupportsElapsedTime => (MachineFeatures & (1u << 12)) != 0;
    public bool SupportsRemainingTime => (MachineFeatures & (1u << 13)) != 0;
    public bool SupportsPowerMeasurement => (MachineFeatures & (1u << 14)) != 0;
    public bool SupportsForceOnBelt => (MachineFeatures & (1u << 15)) != 0;
    public bool SupportsUserDataRetention => (MachineFeatures & (1u << 16)) != 0;

    public bool SupportsTargetSpeed => (TargetSettingFeatures & (1u << 0)) != 0;
    public bool SupportsTargetInclination => (TargetSettingFeatures & (1u << 1)) != 0;
    public bool SupportsTargetResistanceLevel => (TargetSettingFeatures & (1u << 2)) != 0;
    public bool SupportsTargetPower => (TargetSettingFeatures & (1u << 3)) != 0;
    public bool SupportsTargetHeartRate => (TargetSettingFeatures & (1u << 4)) != 0;
    public bool SupportsTargetedExpendedEnergy => (TargetSettingFeatures & (1u << 5)) != 0;
    public bool SupportsTargetedStepNumber => (TargetSettingFeatures & (1u << 6)) != 0;
    public bool SupportsTargetedStrideNumber => (TargetSettingFeatures & (1u << 7)) != 0;
    public bool SupportsTargetedDistance => (TargetSettingFeatures & (1u << 8)) != 0;
    public bool SupportsTargetedTrainingTime => (TargetSettingFeatures & (1u << 9)) != 0;
    public bool SupportsTargetedTimeInTwoHeartRateZones => (TargetSettingFeatures & (1u << 10)) != 0;
    public bool SupportsTargetedTimeInThreeHeartRateZones => (TargetSettingFeatures & (1u << 11)) != 0;
    public bool SupportsTargetedTimeInFiveHeartRateZones => (TargetSettingFeatures & (1u << 12)) != 0;
    public bool SupportsIndoorBikeSimulationParameters => (TargetSettingFeatures & (1u << 13)) != 0;
    public bool SupportsWheelCircumference => (TargetSettingFeatures & (1u << 14)) != 0;
    public bool SupportsSpinDownControl => (TargetSettingFeatures & (1u << 15)) != 0;
    public bool SupportsTargetedCadence => (TargetSettingFeatures & (1u << 16)) != 0;

    public static FtmsDecodeStatus TryDecode(
        ReadOnlySpan<byte> source,
        out FitnessMachineFeature value,
        FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        value = default;
        if (source.Length < EncodedLength)
        {
            return FtmsDecodeStatus.InsufficientData;
        }

        var machineFeatures = BinaryPrimitives.ReadUInt32LittleEndian(source);
        var targetSettingFeatures = BinaryPrimitives.ReadUInt32LittleEndian(source[sizeof(uint)..]);
        if (validationMode == FtmsValidationMode.Strict &&
            ((machineFeatures & ~DefinedMachineFeatureMask) != 0 ||
             (targetSettingFeatures & ~DefinedTargetSettingFeatureMask) != 0))
        {
            return FtmsDecodeStatus.InvalidFlags;
        }

        if (validationMode == FtmsValidationMode.Strict && source.Length != EncodedLength)
        {
            return FtmsDecodeStatus.TrailingData;
        }

        value = new FitnessMachineFeature(machineFeatures, targetSettingFeatures);
        return FtmsDecodeStatus.Success;
    }

    public bool TryEncode(Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (destination.Length < EncodedLength)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, MachineFeatures & DefinedMachineFeatureMask);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[sizeof(uint)..], TargetSettingFeatures & DefinedTargetSettingFeatureMask);
        bytesWritten = EncodedLength;
        return true;
    }
}