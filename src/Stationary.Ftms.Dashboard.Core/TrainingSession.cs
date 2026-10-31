using System.Collections.Immutable;

namespace Stationary.Ftms.Dashboard.Core;

public enum TrainingMachineState
{
    NotStarted,
    Running,
    Paused,
    Stopped,
}

public enum TrainingDestination
{
    Today,
    Ride,
    Training,
    Results,
}

public readonly record struct TrainingMachine(bool IsConnected, TrainingMachineState State)
{
    public bool IsSessionOpen => State is TrainingMachineState.Running or TrainingMachineState.Paused;
}

// Immutable snapshot of a training session; TrainingSessionEngine derives every successor from events.
public sealed record TrainingSession
{
    public static TrainingSession Idle { get; } = new();

    public TrainingWorkoutRunner? Runner { get; init; }

    public TrainingPowerCapabilities Capabilities { get; init; }

    public bool IsRampTest { get; init; }

    public DateTimeOffset? RampStartedAt { get; init; }

    public int PreferredCadenceRpm { get; init; } = TrainingWorkoutCompiler.ReferenceCadenceRpm;

    public string WorkoutEventText { get; init; } = string.Empty;

    public DateTimeOffset? WorkoutStartedAt { get; init; }

    public TrainingMachine Machine { get; init; }

    // Stale device responses are ignored when the generation (session) or revision (target) moved on.
    public long Generation { get; init; }

    public long Revision { get; init; }

    public bool IsSessionStartPending { get; init; }

    public bool IsSessionStartInFlight { get; init; }

    public bool HasSessionStarted { get; init; }

    public bool IsPausePending { get; init; }

    public bool IsResumePending { get; init; }

    public bool IsEnding { get; init; }

    public bool IsEndFailed { get; init; }

    public bool IsLocallyEndable { get; init; }

    public bool IsEndAwaitingStart { get; init; }

    public bool EndHadStarted { get; init; }

    public bool RequiresPedallingBeforeTarget { get; init; }

    public bool IsWaitingForPedalling { get; init; }

    public TrainingCadenceGuard CadenceGuard { get; init; }

    public TrainingResistanceFloor ResistanceFloor { get; init; }

    public int ResistanceFloorNoticeSegmentIndex { get; init; } = -1;

    public DateTimeOffset? SegmentStartedAt { get; init; }

    public TimeSpan SegmentRampDuration { get; init; }

    public TrainingTargetRamp ActiveRamp { get; init; }

    public TimeSpan ActiveRampStartElapsed { get; init; }

    public int ActiveRampSegmentIndex { get; init; } = -1;

    public bool IsRampStepPending { get; init; }

    public int? LastAppliedTargetWatts { get; init; }

    public int CurrentTargetWatts { get; init; }

    public ImmutableList<TelemetrySample> TargetPowerSamples { get; init; } = [];

    public DateTimeOffset LastHeartRateAdjustmentAt { get; init; }

    public bool IsHeartRateAdjustmentPending { get; init; }

    public bool IsHeartRateSignalHandling { get; init; }

    public bool HeartRateProgressRecorded { get; init; }

    public string StatusText { get; init; } = "Choose a workout.";

    public string AdherenceText { get; init; } = TrainingSessionText.CadenceAdherencePending;

    public string CadenceCalibrationText { get; init; } = string.Empty;

    public RampFtpEstimate? RampEstimate { get; init; }

    public TrainingWorkoutState RunnerState => Runner?.State ?? TrainingWorkoutState.Selected;

    public CompiledTrainingWorkout? Workout => Runner?.Workout;

    public bool IsActive => RunnerState is TrainingWorkoutState.ApplyingTarget or TrainingWorkoutState.Running or TrainingWorkoutState.Paused or TrainingWorkoutState.TargetFailed
        || IsEnding
        || IsEndFailed
        || IsLocallyEndable;

    public bool IsStarting => RequiresPedallingBeforeTarget && !IsWaitingForPedalling && RunnerState == TrainingWorkoutState.ApplyingTarget && !IsEnding;

    public bool CanPause => RunnerState == TrainingWorkoutState.Running && !IsPausePending;

    public bool CanResume => RunnerState == TrainingWorkoutState.Paused && !IsResumePending;

    public bool CanRetry => RunnerState == TrainingWorkoutState.TargetFailed;

    public bool CanFinishRampTest => IsRampTest && HasSessionStarted && !IsSessionStartPending && IsActive && !IsEnding;

    public bool IsStalled => RunnerState == TrainingWorkoutState.Running && CadenceGuard.IsStalled;

    public TimeSpan ActiveRampEndElapsed => Runner?.SegmentIndex == ActiveRampSegmentIndex
        ? ActiveRampStartElapsed + ActiveRamp.Duration
        : TimeSpan.Zero;

    public TimeSpan? GetSegmentElapsed(DateTimeOffset timestamp) =>
        Runner is { CurrentSegment: { } segment } runner && runner.GetCurrentSegmentRemaining(timestamp) is { } remaining
            ? segment.Definition.Duration - remaining
            : null;

    public bool IsCadenceTransition(int segmentIndex) =>
        GetSegmentDefinition(segmentIndex) is { } segment
        && TrainingAdherence.ChangesCadenceTarget(GetSegmentDefinition(segmentIndex - 1), segment);

    public bool ChangesPowerTarget(int segmentIndex) =>
        Workout is not { } workout
        || segmentIndex <= 0
        || segmentIndex >= workout.Segments.Count
        || workout.Segments[segmentIndex].TargetWatts != workout.Segments[segmentIndex - 1].TargetWatts;

    public TimeSpan GetSettlingDuration(int segmentIndex) => IsCadenceTransition(segmentIndex)
        ? TrainingAdherence.CadenceTransitionDuration
        : TrainingAdherence.TrainerSettlingDuration;

    private TrainingSegment? GetSegmentDefinition(int segmentIndex) =>
        Workout is { } workout && segmentIndex >= 0 && segmentIndex < workout.Segments.Count
            ? workout.Segments[segmentIndex].Definition
            : null;
}