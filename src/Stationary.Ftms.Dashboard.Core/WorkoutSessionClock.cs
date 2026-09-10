namespace Stationary.Ftms.Dashboard.Core;

public sealed class WorkoutSessionClock
{
    private DateTimeOffset? startedAt;
    private DateTimeOffset? pausedAt;
    private TimeSpan pausedDuration;

    public bool HasStarted => startedAt is not null;

    public bool IsPaused => pausedAt is not null;

    public void Start(DateTimeOffset timestamp)
    {
        startedAt = timestamp;
        pausedAt = null;
        pausedDuration = TimeSpan.Zero;
    }

    public void Pause(DateTimeOffset timestamp)
    {
        if (startedAt is null || pausedAt is not null || timestamp < startedAt)
        {
            return;
        }

        pausedAt = timestamp;
    }

    public void Resume(DateTimeOffset timestamp)
    {
        if (startedAt is null)
        {
            Start(timestamp);
            return;
        }

        if (pausedAt is not { } pauseStartedAt || timestamp < pauseStartedAt)
        {
            return;
        }

        pausedDuration += timestamp - pauseStartedAt;
        pausedAt = null;
    }

    public TimeSpan? GetElapsed(DateTimeOffset timestamp)
    {
        if (startedAt is not { } sessionStartedAt)
        {
            return null;
        }

        var sessionEndedAt = pausedAt ?? timestamp;
        return sessionEndedAt < sessionStartedAt
            ? TimeSpan.Zero
            : sessionEndedAt - sessionStartedAt - pausedDuration;
    }

    public void Reset()
    {
        startedAt = null;
        pausedAt = null;
        pausedDuration = TimeSpan.Zero;
    }
}