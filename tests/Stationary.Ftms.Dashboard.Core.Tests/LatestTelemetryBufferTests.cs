using Stationary.Ftms;
using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class LatestTelemetryBufferTests
{
    [Test]
    public async Task TryPublish_DropsQueuedSnapshotInFavorOfLatest()
    {
        var buffer = new LatestTelemetryBuffer();
        var older = new TelemetrySnapshot(DateTimeOffset.UnixEpoch, FtmsMachineDataType.IndoorBike, 20, null, null, null, null, null);
        var latest = new TelemetrySnapshot(DateTimeOffset.UnixEpoch.AddSeconds(1), FtmsMachineDataType.IndoorBike, 25, null, null, null, null, null);

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

    [Test]
    public async Task Add_EvictsSamplesOutsideRollingWindow()
    {
        var history = new TelemetryHistory(TimeSpan.FromMinutes(5));
        var start = DateTimeOffset.UnixEpoch;

        history.Add(new TelemetrySample(start, 10));
        history.Add(new TelemetrySample(start.AddMinutes(4), 20));
        history.Add(new TelemetrySample(start.AddMinutes(6), 30));

        using (Assert.Multiple())
        {
            await Assert.That(history.Samples).Count().IsEqualTo(2);
            await Assert.That(history.Samples.First().Value).IsEqualTo(20d);
            await Assert.That(history.Samples.Last().Value).IsEqualTo(30d);
        }
    }

    [Test]
    public async Task Trim_EvictsStaleSamplesWithoutAddingAReplacement()
    {
        var history = new TelemetryHistory(TimeSpan.FromMinutes(5));
        var start = DateTimeOffset.UnixEpoch;

        history.Add(new TelemetrySample(start, 10));
        history.Trim(start.AddMinutes(5));

        await Assert.That(history.Samples).Count().IsEqualTo(1);

        history.Trim(start.AddMinutes(5).AddTicks(1));

        await Assert.That(history.Samples).IsEmpty();
    }
}