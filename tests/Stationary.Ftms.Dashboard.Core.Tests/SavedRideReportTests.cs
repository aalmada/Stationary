using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class SavedRideReportTests
{
    [Test]
    public async Task Create_ReportsTheSavedRideInsteadOfLiveCounters()
    {
        var start = new DateTimeOffset(2026, 11, 2, 14, 30, 0, TimeSpan.Zero);
        RecordedTelemetrySample[] samples =
        [
            new(start, 120d, 25d, 85d, 110d),
            new(start.AddSeconds(1), 140d, 26d, 88d, 130d),
            new(start.AddSeconds(2), 130d, 25d, 86d, null),
        ];
        var summary = new SessionSummary(1234d, TimeSpan.FromMinutes(13.5), 132d, 237d,
            new(107.8d, 230d, 200d, 178d, null, 140d, 0.79d, 25.4d));
        var ride = new RecordedSession(1, start.AddMinutes(14), samples, [], summary);

        var report = SavedRideReport.Create(ride, TimeZoneInfo.Utc, useMetricUnits: true);
        var imperial = SavedRideReport.Create(ride with { Samples = [] }, TimeZoneInfo.Utc, useMetricUnits: false);

        using (Assert.Multiple())
        {
            await Assert.That(report.Title).IsEqualTo("Saved Mon 2 Nov, 14:44");
            await Assert.That(report.Distance).IsEqualTo("1234 m");
            await Assert.That(report.Elapsed).IsEqualTo("00:13:30");
            await Assert.That(report.MaximumPower).IsEqualTo("237 W");
            await Assert.That(report.NormalizedPower).IsEqualTo("140 W");
            await Assert.That(report.PeakPower).IsEqualTo("5s 230 W · 30s 200 W · 1m 178 W · 5m --");
            await Assert.That(report.HeartRate).IsEqualTo("120 bpm average · 130 bpm peak");
            await Assert.That(report.Decoupling).IsEqualTo("Ride shorter than 10 minutes");
            await Assert.That(imperial.Distance).IsEqualTo($"{0.77d:F2} mi");
            await Assert.That(imperial.HeartRate).IsEqualTo("No heart-rate data");
            await Assert.That(imperial.Cadence).IsEqualTo("No cadence data");
        }
    }
}