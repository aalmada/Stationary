namespace Stationary.Ftms.Dashboard.Core;

public enum TrainingWorkoutState
{
    Selected,
    ApplyingTarget,
    Running,
    Paused,
    TargetFailed,
    Completed,
    Aborted,
}

// Immutable segment sequencer: each transition returns a new runner, so session state can be reduced without shared mutation.
public sealed record TrainingWorkoutRunner
{
    public TrainingWorkoutRunner(CompiledTrainingWorkout workout)
    {
        Workout = workout ?? throw new ArgumentNullException(nameof(workout));
    }

    public CompiledTrainingWorkout Workout { get; private init; }

    public TrainingWorkoutState State { get; private init; } = TrainingWorkoutState.Selected;

    public int SegmentIndex { get; private init; }

    public TrainingSegmentClock SegmentClock { get; private init; }

    public int SegmentCount => Workout.Segments.Count;

    public CompiledTrainingSegment? CurrentSegment => State is TrainingWorkoutState.Completed or TrainingWorkoutState.Aborted
        ? null
        : Workout.Segments[SegmentIndex];

    public TimeSpan? GetCurrentSegmentRemaining(DateTimeOffset timestamp)
    {
        if (CurrentSegment is not { } segment)
        {
            return null;
        }

        var remaining = segment.Definition.Duration - (SegmentClock.GetElapsed(timestamp) ?? TimeSpan.Zero);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public TrainingWorkoutRunner Start() => State == TrainingWorkoutState.Selected
        ? this with { State = TrainingWorkoutState.ApplyingTarget }
        : this;

    public TrainingWorkoutRunner ConfirmTargetApplied(DateTimeOffset timestamp) => State == TrainingWorkoutState.ApplyingTarget
        ? this with { State = TrainingWorkoutState.Running, SegmentClock = SegmentClock.Start(timestamp) }
        : this;

    public TrainingWorkoutRunner RejectTarget() => State == TrainingWorkoutState.ApplyingTarget
        ? this with { State = TrainingWorkoutState.TargetFailed }
        : this;

    public TrainingWorkoutRunner RetryTarget() => State == TrainingWorkoutState.TargetFailed
        ? this with { State = TrainingWorkoutState.ApplyingTarget }
        : this;

    public TrainingWorkoutRunner Pause(DateTimeOffset timestamp) => State == TrainingWorkoutState.Running
        ? this with { State = TrainingWorkoutState.Paused, SegmentClock = SegmentClock.Pause(timestamp) }
        : this;

    public TrainingWorkoutRunner Resume(DateTimeOffset timestamp) => State == TrainingWorkoutState.Paused
        ? this with { State = TrainingWorkoutState.Running, SegmentClock = SegmentClock.Resume(timestamp) }
        : this;

    // Returns the same instance when the current segment has not finished.
    public TrainingWorkoutRunner Advance(DateTimeOffset timestamp)
    {
        if (State != TrainingWorkoutState.Running
            || SegmentClock.GetElapsed(timestamp) is not { } elapsed
            || elapsed < Workout.Segments[SegmentIndex].Definition.Duration)
        {
            return this;
        }

        var next = SegmentIndex + 1;
        return this with
        {
            SegmentIndex = next,
            SegmentClock = TrainingSegmentClock.Stopped,
            State = next == Workout.Segments.Count ? TrainingWorkoutState.Completed : TrainingWorkoutState.ApplyingTarget,
        };
    }

    // Swaps in revised segment metadata, such as recalibrated cadence ranges, without disturbing progress.
    public TrainingWorkoutRunner ReplaceWorkout(CompiledTrainingWorkout updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        return updated.Segments.Count == Workout.Segments.Count
            ? this with { Workout = updated }
            : throw new ArgumentException("A replacement workout must keep the same segments.", nameof(updated));
    }

    public TrainingWorkoutRunner Abort() => State is TrainingWorkoutState.Completed or TrainingWorkoutState.Aborted
        ? this
        : this with { State = TrainingWorkoutState.Aborted, SegmentClock = TrainingSegmentClock.Stopped };
}