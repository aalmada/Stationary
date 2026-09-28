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
}