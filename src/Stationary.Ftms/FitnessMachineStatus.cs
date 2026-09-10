namespace Stationary.Ftms;

public enum FtmsMachineStatusOpcode : byte
{
    Reset = 1,
    StoppedOrPausedByUser,
    StoppedBySafetyKey,
    StartedOrResumedByUser,
    TargetSpeedChanged,
    TargetInclineChanged,
    TargetResistanceLevelChanged,
    TargetPowerChanged,
    TargetHeartRateChanged,
    TargetedExpendedEnergyChanged,
    TargetedStepsChanged,
    TargetedStridesChanged,
    TargetedDistanceChanged,
    TargetedTrainingTimeChanged,
    TargetedTimeInTwoHeartRateZonesChanged,
    TargetedTimeInThreeHeartRateZonesChanged,
    TargetedTimeInFiveHeartRateZonesChanged,
    IndoorBikeSimulationParametersChanged,
    WheelCircumferenceChanged,
    SpinDownStatus,
    TargetedCadenceChanged,
    ControlPermissionLost = 0xFF,
}

public static class FitnessMachineStatus
{
    public static FtmsDecodeStatus TryDecode(ReadOnlySpan<byte> source, out FtmsMachineStatusOpcode opcode, out ReadOnlySpan<byte> parameters, FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        opcode = default;
        parameters = default;
        if (source.IsEmpty) return FtmsDecodeStatus.InsufficientData;
        opcode = (FtmsMachineStatusOpcode)source[0];
        var length = GetParameterLength(opcode);
        if (length < 0) return FtmsDecodeStatus.InvalidFlags;
        if (source.Length - 1 < length) return FtmsDecodeStatus.InsufficientData;
        if (validationMode == FtmsValidationMode.Strict && source.Length != length + 1) return FtmsDecodeStatus.TrailingData;
        parameters = source.Slice(1, length);
        return FtmsDecodeStatus.Success;
    }

    private static int GetParameterLength(FtmsMachineStatusOpcode opcode) => opcode switch
    {
        FtmsMachineStatusOpcode.Reset or FtmsMachineStatusOpcode.StoppedBySafetyKey or FtmsMachineStatusOpcode.StartedOrResumedByUser or FtmsMachineStatusOpcode.ControlPermissionLost => 0,
        FtmsMachineStatusOpcode.StoppedOrPausedByUser or FtmsMachineStatusOpcode.TargetHeartRateChanged or FtmsMachineStatusOpcode.SpinDownStatus => 1,
        FtmsMachineStatusOpcode.TargetSpeedChanged or FtmsMachineStatusOpcode.TargetInclineChanged or FtmsMachineStatusOpcode.TargetResistanceLevelChanged or FtmsMachineStatusOpcode.TargetPowerChanged or FtmsMachineStatusOpcode.TargetedExpendedEnergyChanged or FtmsMachineStatusOpcode.TargetedStepsChanged or FtmsMachineStatusOpcode.TargetedStridesChanged or FtmsMachineStatusOpcode.TargetedTrainingTimeChanged or FtmsMachineStatusOpcode.WheelCircumferenceChanged or FtmsMachineStatusOpcode.TargetedCadenceChanged => 2,
        FtmsMachineStatusOpcode.TargetedDistanceChanged => 3,
        FtmsMachineStatusOpcode.TargetedTimeInTwoHeartRateZonesChanged => 4,
        FtmsMachineStatusOpcode.TargetedTimeInThreeHeartRateZonesChanged or FtmsMachineStatusOpcode.IndoorBikeSimulationParametersChanged => 6,
        FtmsMachineStatusOpcode.TargetedTimeInFiveHeartRateZonesChanged => 10,
        _ => -1,
    };
}