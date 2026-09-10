using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class HeartRateSessionAnalyticsTests
{
    [Test]
    public async Task Add_AccumulatesTimeInThePreviousZone()
    {
        var analytics = new HeartRateSessionAnalytics(CreateProfile(), TimeSpan.FromSeconds(5));
        var start = DateTimeOffset.UnixEpoch;

        analytics.Add(new(start, 120));
        analytics.Add(new(start.AddSeconds(3), 135));
        analytics.Add(new(start.AddSeconds(5), 140));

        var durations = analytics.GetTimeInZones();
        using (Assert.Multiple())
        {
            await Assert.That(durations[0].Duration).IsEqualTo(TimeSpan.FromSeconds(3));
            await Assert.That(durations[1].Duration).IsEqualTo(TimeSpan.FromSeconds(2));
            await Assert.That(analytics.AverageBeatsPerMinute).IsEqualTo(395d / 3d);
            await Assert.That(analytics.MaximumBeatsPerMinute).IsEqualTo((ushort)140);
        }
    }

    [Test]
    public async Task Add_DoesNotCountTimeAcrossTelemetryGap()
    {
        var analytics = new HeartRateSessionAnalytics(CreateProfile(), TimeSpan.FromSeconds(5));
        var start = DateTimeOffset.UnixEpoch;

        analytics.Add(new(start, 120));
        analytics.Add(new(start.AddSeconds(3), 135));
        analytics.Add(new(start.AddSeconds(10), 140));

        var durations = analytics.GetTimeInZones();
        using (Assert.Multiple())
        {
            await Assert.That(durations[0].Duration).IsEqualTo(TimeSpan.FromSeconds(3));
            await Assert.That(durations[1].Duration).IsEqualTo(TimeSpan.Zero);
        }
    }

    [Test]
    public async Task PauseAndResume_ExcludeObservationsReceivedWhilePaused()
    {
        var analytics = new HeartRateSessionAnalytics(CreateProfile(), TimeSpan.FromSeconds(5));
        var start = DateTimeOffset.UnixEpoch;

        analytics.Add(new(start, 120));
        analytics.Pause(start.AddSeconds(3));
        analytics.Add(new(start.AddSeconds(4), 135));
        analytics.Resume();
        analytics.Add(new(start.AddSeconds(5), 140));
        analytics.Add(new(start.AddSeconds(7), 140));

        var durations = analytics.GetTimeInZones();
        using (Assert.Multiple())
        {
            await Assert.That(durations[0].Duration).IsEqualTo(TimeSpan.FromSeconds(3));
            await Assert.That(durations[1].Duration).IsEqualTo(TimeSpan.FromSeconds(2));
            await Assert.That(analytics.AverageBeatsPerMinute).IsEqualTo(400d / 3d);
        }
    }

    [Test]
    public async Task CreateCyclingLactateThreshold_MapsThresholdBoundariesToExpectedZones()
    {
        var profile = HeartRateZoneProfile.CreateCyclingLactateThreshold(170);

        using (Assert.Multiple())
        {
            await Assert.That(profile.GetZone(137).Code).IsEqualTo("Z1");
            await Assert.That(profile.GetZone(138).Code).IsEqualTo("Z2");
            await Assert.That(profile.GetZone(170).Code).IsEqualTo("Z5a");
            await Assert.That(profile.GetZone(181).Code).IsEqualTo("Z5c");
        }
    }

    private static HeartRateZoneProfile CreateProfile() => new("Test", [
        new("Z1", "Easy", 0, 129),
        new("Z2", "Hard", 130, null),
    ]);
}