namespace Stationary.Ftms.Dashboard.Core;

// Immutable pausable stopwatch for one training segment; every transition returns a new value.
public readonly record struct TrainingSegmentClock(DateTimeOffset? StartedAt, DateTimeOffset? PausedAt, TimeSpan PausedDuration)
{
    public static TrainingSegmentClock Stopped => default;

    public bool HasStarted => StartedAt is not null;

    public bool IsPaused => PausedAt is not null;

    public TrainingSegmentClock Start(DateTimeOffset timestamp) => new(timestamp, null, TimeSpan.Zero);

    public TrainingSegmentClock Pause(DateTimeOffset timestamp) =>
        StartedAt is { } startedAt && PausedAt is null && timestamp >= startedAt
            ? this with { PausedAt = timestamp }
            : this;

    public TrainingSegmentClock Resume(DateTimeOffset timestamp) => (StartedAt, PausedAt) switch
    {
        (null, _) => Start(timestamp),
        (_, { } pausedAt) when timestamp >= pausedAt => this with { PausedAt = null, PausedDuration = PausedDuration + (timestamp - pausedAt) },
        _ => this,
    };

    public TimeSpan? GetElapsed(DateTimeOffset timestamp)
    {
        if (StartedAt is not { } startedAt)
        {
            return null;
        }

        var endedAt = PausedAt ?? timestamp;
        return endedAt < startedAt ? TimeSpan.Zero : endedAt - startedAt - PausedDuration;
    }
}