using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class RampFtpProtocolTests
{
    [Test]
    public async Task Create_AlignsEveryTargetToTheBikeIncrement()
    {
        var workout = RampFtpProtocol.Create(new(25, 200, 5));

        using (Assert.Multiple())
        {
            await Assert.That(workout.Segments[0].TargetWatts).IsEqualTo(50);
            await Assert.That(workout.Segments.Skip(1).All(segment => (segment.TargetWatts - 25) % 5 == 0)).IsTrue();
            await Assert.That(workout.Segments[^1].TargetWatts).IsEqualTo(200);
        }
    }

    [Test]
    public async Task Create_ClampsTheStepIndexToTheHighestSupportedTarget()
    {
        var workout = RampFtpProtocol.Create(new(55, 101, 10));

        using (Assert.Multiple())
        {
            await Assert.That(workout.Segments.Skip(1).All(segment => segment.TargetWatts is 55 or 65 or 75 or 85 or 95)).IsTrue();
            await Assert.That(workout.Segments[^1].TargetWatts).IsEqualTo(95);
        }
    }
}