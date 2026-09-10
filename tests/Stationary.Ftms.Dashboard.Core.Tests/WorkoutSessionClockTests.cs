using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class WorkoutSessionClockTests
{
    [Test]
    public async Task PauseAndResume_ExcludesThePausedDuration()
    {
        var start = DateTimeOffset.UnixEpoch;
        var clock = new WorkoutSessionClock();

        clock.Start(start);
        clock.Pause(start.AddSeconds(10));
        var elapsedWhilePaused = clock.GetElapsed(start.AddSeconds(30));
        clock.Resume(start.AddSeconds(30));
        var elapsedAfterResuming = clock.GetElapsed(start.AddSeconds(35));

        using (Assert.Multiple())
        {
            await Assert.That(elapsedWhilePaused).IsEqualTo(TimeSpan.FromSeconds(10));
            await Assert.That(elapsedAfterResuming).IsEqualTo(TimeSpan.FromSeconds(15));
        }
    }
}