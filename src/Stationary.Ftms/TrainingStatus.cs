namespace Stationary.Ftms;

public enum FtmsTrainingState : byte
{
    Other, Idle, WarmingUp, LowIntensityInterval, HighIntensityInterval, RecoveryInterval, Isometric, HeartRateControl, FitnessTest,
    SpeedOutsideControlRegionLow, SpeedOutsideControlRegionHigh, CoolDown, WattControl, ManualMode, PreWorkout, PostWorkout,
}

public readonly record struct TrainingStatus(FtmsTrainingState State, bool HasExtendedString)
{
    public static FtmsDecodeStatus TryDecode(ReadOnlySpan<byte> source, out TrainingStatus value, out ReadOnlySpan<byte> utf8Description, FtmsValidationMode validationMode = FtmsValidationMode.Compatible)
    {
        value = default;
        utf8Description = default;
        if (source.Length < 2) return FtmsDecodeStatus.InsufficientData;
        var flags = source[0];
        if (validationMode == FtmsValidationMode.Strict && ((flags & 0xFC) != 0 || source[1] > (byte)FtmsTrainingState.PostWorkout)) return FtmsDecodeStatus.InvalidFlags;
        var hasDescription = (flags & 1) != 0;
        if (!hasDescription && validationMode == FtmsValidationMode.Strict && source.Length != 2) return FtmsDecodeStatus.TrailingData;
        value = new TrainingStatus((FtmsTrainingState)source[1], (flags & 2) != 0);
        if (hasDescription) utf8Description = source[2..];
        return FtmsDecodeStatus.Success;
    }
}