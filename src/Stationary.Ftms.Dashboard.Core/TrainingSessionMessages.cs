namespace Stationary.Ftms.Dashboard.Core;

// Telemetry sampled on each clock tick. Values are null when stale; HeartRate is null without a live sensor.
public readonly record struct TrainingTelemetry(double? CadenceRpm, double? PowerWatts, HeartRateObservation? HeartRate, ushort? SafetyCeilingHeartRate);

public enum TrainingTargetPurpose
{
    SegmentTarget,
    PedallingHold,
    HeldTarget,
    RampStep,
    HeartRateAdjustment,
    HeartRateSafeTarget,
}

public enum TrainingPausePurpose
{
    Rider,
    HeartRateSignalLoss,
}

public enum TrainingEndPurpose
{
    Rider,
    WorkoutComplete,
    RampTestFinished,
    RampTestExhausted,
}

public abstract record TrainingSessionEvent(DateTimeOffset At);

public sealed record WorkoutBegun(
    DateTimeOffset At,
    CompiledTrainingWorkout Workout,
    TrainingPowerCapabilities Capabilities,
    bool IsRampTest,
    int PreferredCadenceRpm,
    string FunctionalThresholdPowerText) : TrainingSessionEvent(At);

public sealed record ClockTicked(DateTimeOffset At, TrainingTelemetry Telemetry) : TrainingSessionEvent(At);

public sealed record MachineStateChanged(DateTimeOffset At, TrainingMachine Machine) : TrainingSessionEvent(At);

public sealed record PauseRequested(DateTimeOffset At) : TrainingSessionEvent(At);

public sealed record ResumeRequested(DateTimeOffset At) : TrainingSessionEvent(At);

public sealed record RetryRequested(DateTimeOffset At) : TrainingSessionEvent(At);

public sealed record EndRequested(DateTimeOffset At) : TrainingSessionEvent(At);

public sealed record RampTestFinishRequested(DateTimeOffset At) : TrainingSessionEvent(At);

public sealed record NoticePosted(DateTimeOffset At, string Text) : TrainingSessionEvent(At);

public sealed record PowerTargetCompleted(DateTimeOffset At, ApplyPowerTarget Request, bool Accepted) : TrainingSessionEvent(At);

public sealed record MachineSessionStarted(DateTimeOffset At, StartMachineSession Request, bool Accepted, string ConnectionStatus) : TrainingSessionEvent(At);

public sealed record MachineSessionPaused(DateTimeOffset At, PauseMachineSession Request, bool Accepted) : TrainingSessionEvent(At);

public sealed record MachineSessionResumed(DateTimeOffset At, bool Accepted) : TrainingSessionEvent(At);

public sealed record MachineSessionEnded(DateTimeOffset At, EndMachineSession Request, bool Succeeded) : TrainingSessionEvent(At);

public sealed record HeartRateVolumeRecorded(DateTimeOffset At, RecordHeartRateVolume Request, bool Recorded, string Text) : TrainingSessionEvent(At);

public sealed record OperationFailed(DateTimeOffset At, string Message) : TrainingSessionEvent(At);

public abstract record TrainingSessionEffect;

public sealed record AddTrainingEvent(string Text) : TrainingSessionEffect;

public sealed record NavigateTo(TrainingDestination Destination) : TrainingSessionEffect;

public sealed record ResetSafetyAcknowledgements : TrainingSessionEffect;

public sealed record RecordHeartRateVolume(DateTimeOffset StartedAt, string StatusPrefix) : TrainingSessionEffect;

public sealed record StartMachineSession(long Generation, bool AnnounceWorkout) : TrainingSessionEffect;

public sealed record PauseMachineSession(TrainingPausePurpose Purpose, long Generation, bool SafeTargetAccepted = false) : TrainingSessionEffect;

public sealed record ResumeMachineSession : TrainingSessionEffect;

public sealed record EndMachineSession(TrainingEndPurpose Purpose) : TrainingSessionEffect;

public sealed record ApplyPowerTarget(
    TrainingTargetPurpose Purpose,
    long Generation,
    long Revision,
    int SegmentIndex,
    int Watts,
    TrainingTargetRamp Ramp = default,
    string? AcceptedNotice = null) : TrainingSessionEffect;