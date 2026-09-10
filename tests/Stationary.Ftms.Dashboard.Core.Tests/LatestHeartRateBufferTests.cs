using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class LatestHeartRateBufferTests
{
    [Test]
    public async Task TryPublish_DropsQueuedObservationInFavorOfLatest()
    {
        var buffer = new LatestHeartRateBuffer();
        var older = new HeartRateObservation(DateTimeOffset.UnixEpoch, 120);
        var latest = new HeartRateObservation(DateTimeOffset.UnixEpoch.AddSeconds(1), 135);

        var olderAccepted = buffer.TryPublish(older);
        var latestAccepted = buffer.TryPublish(latest);
        var received = await buffer.Reader.ReadAsync(CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(olderAccepted).IsTrue();
            await Assert.That(latestAccepted).IsTrue();
            await Assert.That(received).IsEqualTo(latest);
        }
    }
}