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

public sealed class TrainingWorkoutRunner
{
    private readonly CompiledTrainingWorkout workout;
    private readonly WorkoutSessionClock segmentClock = new();
    private int segmentIndex;
    private TrainingWorkoutState state = TrainingWorkoutState.Selected;

    public TrainingWorkoutRunner(CompiledTrainingWorkout workout)
    {
        this.workout = workout ?? throw new ArgumentNullException(nameof(workout));
    }

    public TrainingWorkoutState State => state;

    public int SegmentIndex => segmentIndex;

    public CompiledTrainingSegment? CurrentSegment => state is TrainingWorkoutState.Completed or TrainingWorkoutState.Aborted
        ? null
        : workout.Segments[segmentIndex];

    public void Start()
    {
        if (state == TrainingWorkoutState.Selected)
        {
            state = TrainingWorkoutState.ApplyingTarget;
        }
    }

    public void ConfirmTargetApplied(DateTimeOffset timestamp)
    {
        if (state != TrainingWorkoutState.ApplyingTarget)
        {
            return;
        }

        segmentClock.Start(timestamp);
        state = TrainingWorkoutState.Running;
    }

    public void RejectTarget()
    {
        if (state == TrainingWorkoutState.ApplyingTarget)
        {
            state = TrainingWorkoutState.TargetFailed;
        }
    }

    public void RetryTarget()
    {
        if (state == TrainingWorkoutState.TargetFailed)
        {
            state = TrainingWorkoutState.ApplyingTarget;
        }
    }

    public void Pause(DateTimeOffset timestamp)
    {
        if (state == TrainingWorkoutState.Running)
        {
            segmentClock.Pause(timestamp);
            state = TrainingWorkoutState.Paused;
        }
    }

    public void Resume(DateTimeOffset timestamp)
    {
        if (state == TrainingWorkoutState.Paused)
        {
            segmentClock.Resume(timestamp);
            state = TrainingWorkoutState.Running;
        }
    }

    public bool Advance(DateTimeOffset timestamp)
    {
        if (state != TrainingWorkoutState.Running || segmentClock.GetElapsed(timestamp) is not { } elapsed || elapsed < workout.Segments[segmentIndex].Definition.Duration)
        {
            return false;
        }

        segmentIndex++;
        segmentClock.Reset();
        state = segmentIndex == workout.Segments.Count ? TrainingWorkoutState.Completed : TrainingWorkoutState.ApplyingTarget;
        return true;
    }

    public void Abort()
    {
        if (state is not TrainingWorkoutState.Completed and not TrainingWorkoutState.Aborted)
        {
            segmentClock.Reset();
            state = TrainingWorkoutState.Aborted;
        }
    }
}