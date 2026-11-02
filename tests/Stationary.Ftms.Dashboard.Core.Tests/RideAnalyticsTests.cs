using System.Text.Json;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class RideAnalyticsTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 14, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaximumGap = TimeSpan.FromSeconds(5);

    private static readonly RideHeartRateZone[] Zones =
    [
        new("Z1", "Recovery", 100, 129),
        new("Z2", "Endurance", 130, 159),
        new("Z3", "Tempo", 160, null),
    ];

    [Test]
    public async Task GetHeartRateZoneTimes_SplitsTimeByTheZoneOfEachSample()
    {
        var samples = CreateSamples(121, second => (150d, second < 60 ? 110d : 140d));

        var times = RideAnalytics.GetHeartRateZoneTimes(samples, Zones, MaximumGap);

        using (Assert.Multiple())
        {
            await Assert.That(times).Count().IsEqualTo(3);
            await Assert.That(times[0].Duration).IsEqualTo(TimeSpan.FromSeconds(60));
            await Assert.That(times[0].Fraction).IsEqualTo(0.5d);
            await Assert.That(times[0].Label).IsEqualTo("Z1 Recovery · 100–129 bpm");
            await Assert.That(times[1].DurationText).IsEqualTo("1:00");
            await Assert.That(times[1].PercentText).IsEqualTo("50%");
            await Assert.That(times[2].Duration).IsEqualTo(TimeSpan.Zero);
        }
    }

    [Test]
    public async Task GetHeartRateZoneTimes_IgnoresGapsAndMissingHeartRate()
    {
        RecordedTelemetrySample[] samples =
        [
            new(Start, 150d, null, null, 110d),
            new(Start.AddSeconds(10), 150d, null, null, 110d),
            new(Start.AddSeconds(11), 150d, null, null, null),
            new(Start.AddSeconds(12), 150d, null, null, 140d),
        ];

        var times = RideAnalytics.GetHeartRateZoneTimes(samples, Zones, MaximumGap);
        var none = RideAnalytics.GetHeartRateZoneTimes(samples[2..], Zones, MaximumGap);

        using (Assert.Multiple())
        {
            await Assert.That(times[0].Duration).IsEqualTo(TimeSpan.FromSeconds(1));
            await Assert.That(times[1].Duration).IsEqualTo(TimeSpan.Zero);
            await Assert.That(none).IsEmpty();
        }
    }

    [Test]
    public async Task GetPowerZoneTimes_UsesTheFunctionalThresholdPowerZones()
    {
        var profile = PowerZoneProfile.Create(200);
        var samples = CreateSamples(91, second => (second < 30 ? 100d : 200d, null));

        var times = RideAnalytics.GetPowerZoneTimes(samples, profile, MaximumGap);
        var easy = times.Single(zone => zone.Code == profile.GetZone(100d).Code);
        var threshold = times.Single(zone => zone.Code == profile.GetZone(200d).Code);

        using (Assert.Multiple())
        {
            await Assert.That(times).Count().IsEqualTo(profile.Zones.Count);
            await Assert.That(easy.Duration).IsEqualTo(TimeSpan.FromSeconds(30));
            await Assert.That(threshold.Duration).IsEqualTo(TimeSpan.FromSeconds(60));
        }
    }

    [Test]
    public async Task GetAdherence_ScoresPowerAndHeartRateAfterTheSettlingTime()
    {
        var samples = CreateSamples(181, second => second switch
        {
            < 40 => (200d, 120d),
            < 60 => (300d, 120d),
            < 105 => (150d, 130d),
            _ => (150d, 150d),
        });
        RideTargetSegment[] targets =
        [
            new(0, Start, "Warm-up", 200, null, null, IsHeartRateLed: false, IsRampStep: false),
            new(1, Start.AddSeconds(60), "Endurance", 150, 120, 140, IsHeartRateLed: true, IsRampStep: false),
            new(2, Start.AddSeconds(120), "Step 1", 250, null, null, IsHeartRateLed: false, IsRampStep: true),
        ];
        var session = CreateSession(samples, targets);

        var adherence = RideAnalytics.GetAdherence(session, MaximumGap);

        await Assert.That(adherence).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(adherence!.Segments).Count().IsEqualTo(2);
            await Assert.That(adherence.Segments[0].PowerFraction.GetValueOrDefault()).IsBetween(25d / 45d - 0.001d, 25d / 45d + 0.001d);
            await Assert.That(adherence.Segments[0].HeartRateFraction).IsNull();
            await Assert.That(adherence.Segments[0].TargetText).IsEqualTo("200 W");
            await Assert.That(adherence.Segments[1].PowerFraction).IsNull();
            await Assert.That(adherence.Segments[1].HeartRateFraction.GetValueOrDefault()).IsBetween(30d / 45d - 0.001d, 30d / 45d + 0.001d);
            await Assert.That(adherence.Segments[1].TargetText).IsEqualTo("HR 120–140 bpm");
            await Assert.That(adherence.Segments[1].ResultText).IsEqualTo("HR 67%");
            await Assert.That(adherence.HeartRateFraction.GetValueOrDefault()).IsBetween(0.666d, 0.667d);
        }
    }

    [Test]
    public async Task GetAdherence_EndsTheLastSegmentAtTheStopEvent()
    {
        var samples = CreateSamples(121, second => (second < 60 ? 200d : 0d, null));
        RideTargetSegment[] targets = [new(0, Start, "Steady", 200, null, null, IsHeartRateLed: false, IsRampStep: false)];
        var session = CreateSession(samples, targets) with
        {
            Events = [new(Start, SessionEventType.Start, "Start"), new(Start.AddSeconds(60), SessionEventType.Stop, "Stop")],
        };

        var adherence = RideAnalytics.GetAdherence(session, MaximumGap);
        var free = RideAnalytics.GetAdherence(session with { Context = null }, MaximumGap);

        using (Assert.Multiple())
        {
            await Assert.That(adherence!.PowerFraction).IsEqualTo(1d);
            await Assert.That(adherence.Segments[0].MeasuredDuration).IsEqualTo(TimeSpan.FromSeconds(45));
            await Assert.That(free).IsNull();
        }
    }

    [Test]
    public async Task GetChartSeries_AveragesSamplesAndDrawsTargetSteps()
    {
        var samples = CreateSamples(1_200, second => (second < 600 ? 100d : 200d, 130d));
        RideTargetSegment[] targets =
        [
            new(0, Start, "Easy", 100, null, null, IsHeartRateLed: false, IsRampStep: false),
            new(1, Start.AddSeconds(600), "Hard", 200, null, null, IsHeartRateLed: false, IsRampStep: false),
        ];

        var series = RideAnalytics.GetChartSeries(CreateSession(samples, targets), maximumPoints: 300);
        var empty = RideAnalytics.GetChartSeries(CreateSession([], []));

        using (Assert.Multiple())
        {
            await Assert.That(series.Power.Count).IsLessThanOrEqualTo(301);
            await Assert.That(series.Power[0].Value).IsEqualTo(100d);
            await Assert.That(series.Power[^1].Value).IsEqualTo(200d);
            await Assert.That(series.HeartRate.Count).IsEqualTo(series.Power.Count);
            await Assert.That(series.Cadence).IsEmpty();
            await Assert.That(series.TargetPower).Count().IsEqualTo(4);
            await Assert.That(series.TargetPower[1].CapturedAt).IsEqualTo(Start.AddSeconds(600));
            await Assert.That(series.Start).IsEqualTo(Start);
            await Assert.That(series.End).IsEqualTo(Start.AddSeconds(1_199));
            await Assert.That(empty).IsSameReferenceAs(RideChartSeries.Empty);
        }
    }

    [Test]
    public async Task Calculate_SkipsDecouplingWhenPowerVaries()
    {
        var samples = CreateSamples(1_201, second => ((second / 60) % 2 == 0 ? 100d : 300d, 130d));

        var metrics = WorkoutAnalysis.Calculate(samples);
        var normalized = WorkoutAnalysis.GetNormalizedPower(samples);

        using (Assert.Multiple())
        {
            await Assert.That(metrics.DecouplingAvailability).IsEqualTo(DecouplingAvailability.Variable);
            await Assert.That(metrics.AerobicDecouplingPercentage).IsNull();
            await Assert.That(metrics.VariabilityIndex.GetValueOrDefault()).IsGreaterThan(1.05d);
            await Assert.That(normalized.GetValueOrDefault()).IsGreaterThan(200d);
            await Assert.That(WorkoutAnalysis.GetVariabilityIndex(210d, 200d)).IsEqualTo(1.05d);
            await Assert.That(WorkoutAnalysis.GetVariabilityIndex(null, 200d)).IsNull();
        }
    }

    [Test]
    public async Task SavedRideReport_UsesTheSettingsStoredWithTheRide()
    {
        var samples = CreateSamples(1_201, second => (second < 600 ? 120d : 180d, second < 600 ? 120d : 140d));
        RideTargetSegment[] targets = [new(0, Start, "Endurance", 150, null, 140, IsHeartRateLed: false, IsRampStep: false)];
        var session = CreateSession(samples, targets);

        var report = SavedRideReport.Create(session, TimeZoneInfo.Utc, useMetricUnits: true);
        var bare = SavedRideReport.Create(session with { Context = null }, TimeZoneInfo.Utc, useMetricUnits: true);
        var fallback = SavedRideReport.Create(session with { Context = null }, TimeZoneInfo.Utc, useMetricUnits: true, currentFunctionalThresholdPowerWatts: 200);

        using (Assert.Multiple())
        {
            await Assert.That(report.HasHeartRateZones).IsTrue();
            await Assert.That(report.HasPowerZones).IsTrue();
            await Assert.That(report.HasAdherence).IsTrue();
            await Assert.That(report.HasCharts).IsTrue();
            await Assert.That(report.HasHeartRateChart).IsTrue();
            await Assert.That(report.HeartRateZones.Sum(zone => zone.Duration.Ticks)).IsEqualTo(TimeSpan.FromSeconds(1_200).Ticks);
            await Assert.That(bare.HasHeartRateZones).IsFalse();
            await Assert.That(bare.HasPowerZones).IsFalse();
            await Assert.That(bare.HasAdherence).IsFalse();
            await Assert.That(fallback.HasPowerZones).IsTrue();
        }
    }

    [Test]
    public async Task RecordedSession_RoundTripsTheRideContext()
    {
        RideTargetSegment[] targets = [new(0, Start, "Endurance", 150, 120, 140, IsHeartRateLed: true, IsRampStep: false)];
        var session = CreateSession(CreateSamples(3, _ => (150d, 125d)), targets);

        var json = JsonSerializer.Serialize(session, RecordedSessionJsonContext.Default.RecordedSession);
        var restored = JsonSerializer.Deserialize(json, RecordedSessionJsonContext.Default.RecordedSession)!;

        using (Assert.Multiple())
        {
            await Assert.That(restored.FormatVersion).IsEqualTo(RecordedSession.CurrentFormatVersion);
            await Assert.That(restored.Context!.FunctionalThresholdPowerWatts).IsEqualTo((ushort)200);
            await Assert.That(restored.Context.HeartRateZones!).IsEquivalentTo(Zones);
            await Assert.That(restored.Context.Targets!).IsEquivalentTo(targets);
            await Assert.That(restored.StartedAt).IsEqualTo(Start);
        }
    }

    internal static RecordedTelemetrySample[] CreateSamples(int count, Func<int, (double? Power, double? HeartRate)> values) =>
        [.. Enumerable.Range(0, count).Select(second =>
        {
            var (power, heartRate) = values(second);
            return new RecordedTelemetrySample(Start.AddSeconds(second), power, null, null, heartRate);
        })];

    private static RecordedSession CreateSession(RecordedTelemetrySample[] samples, RideTargetSegment[] targets)
    {
        var elapsed = samples.Length > 0 ? samples[^1].CapturedAt - samples[0].CapturedAt : TimeSpan.Zero;
        var average = samples.Length > 0 ? samples.Average(static sample => sample.PowerWatts ?? 0d) : 0d;
        var summary = new SessionSummary(0d, elapsed, average, average, new(0d, null, null, null, null, WorkoutAnalysis.GetNormalizedPower(samples), null, null));
        return new(
            RecordedSession.CurrentFormatVersion,
            Start.Add(elapsed),
            samples,
            [],
            summary,
            new RideContext(200, "Karvonen", Zones, targets));
    }
}