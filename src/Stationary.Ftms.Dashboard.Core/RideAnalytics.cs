using System.Globalization;

namespace Stationary.Ftms.Dashboard.Core;

public sealed record RideZoneShare(string Code, string Label, TimeSpan Duration, double Fraction)
{
    public string DurationText => Duration.ToString(Duration >= TimeSpan.FromHours(1) ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    public string PercentText => string.Create(CultureInfo.InvariantCulture, $"{Fraction * 100d:F0}%");
}

public sealed record RideSegmentAdherence(
    int SegmentIndex,
    string Name,
    string TargetText,
    TimeSpan MeasuredDuration,
    double? PowerFraction,
    double? HeartRateFraction)
{
    public string ResultText => string.Join(" · ", new[]
    {
        PowerFraction is double power ? string.Create(CultureInfo.InvariantCulture, $"power {power * 100d:F0}%") : null,
        HeartRateFraction is double heartRate ? string.Create(CultureInfo.InvariantCulture, $"HR {heartRate * 100d:F0}%") : null,
    }.OfType<string>());
}

public sealed record RideAdherence(IReadOnlyList<RideSegmentAdherence> Segments, double? PowerFraction, double? HeartRateFraction);

public sealed record RideChartSeries(
    IReadOnlyList<TelemetrySample> Power,
    IReadOnlyList<TelemetrySample> HeartRate,
    IReadOnlyList<TelemetrySample> Cadence,
    IReadOnlyList<TelemetrySample> TargetPower,
    DateTimeOffset? Start,
    DateTimeOffset? End)
{
    public static RideChartSeries Empty { get; } = new([], [], [], [], null, null);
}

public static class RideAnalytics
{
    // Trainers take a few seconds to reach a new target and heart rate lags further, so the start of each segment is not scored.
    public static readonly TimeSpan SettlingTime = TimeSpan.FromSeconds(15);

    public const double PowerTolerance = 0.10d;
    public const double MinimumPowerToleranceWatts = 10d;

    public static IReadOnlyList<RideZoneShare> GetHeartRateZoneTimes(
        IReadOnlyList<RecordedTelemetrySample> samples,
        IReadOnlyList<RideHeartRateZone> zones,
        TimeSpan maximumGap)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(zones);
        return GetZoneTimes(
            samples,
            [.. zones.Select(static zone => (zone.Code, $"{zone.Code} {zone.Name} · {FormatHeartRateRange(zone)}"))],
            sample => sample.HeartRateBeatsPerMinute is double bpm and > 0d ? FindHeartRateZone(zones, bpm) : null,
            maximumGap);
    }

    public static IReadOnlyList<RideZoneShare> GetPowerZoneTimes(
        IReadOnlyList<RecordedTelemetrySample> samples,
        PowerZoneProfile profile,
        TimeSpan maximumGap)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(profile);
        var zones = profile.Zones;
        return GetZoneTimes(
            samples,
            [.. zones.Select(static zone => (zone.Code, $"{zone.Code} {zone.Name} · {FormatPowerRange(zone)}"))],
            sample => sample.PowerWatts is double watts ? IndexOf(zones, profile.GetZone(watts)) : null,
            maximumGap);
    }

    public static RideAdherence? GetAdherence(RecordedSession session, TimeSpan maximumGap)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Context?.Targets is not { Length: > 0 } targets)
        {
            return null;
        }

        var ordered = targets.OrderBy(static target => target.StartedAt).ToArray();
        var segments = new List<RideSegmentAdherence>();
        long powerTicks = 0, powerOnTargetTicks = 0, heartRateTicks = 0, heartRateInRangeTicks = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var target = ordered[index];
            if (target.IsRampStep)
            {
                continue;
            }

            var end = GetSegmentEnd(session, ordered, index);
            var scoredStart = target.StartedAt + SettlingTime;
            var scorePower = !target.IsHeartRateLed && target.TargetWatts > 0;
            var scoreHeartRate = target.MinimumHeartRate is not null || target.MaximumHeartRate is not null;
            long segmentTicks = 0, segmentPower = 0, segmentPowerHit = 0, segmentHeartRate = 0, segmentHeartRateHit = 0;
            var tolerance = double.Max(MinimumPowerToleranceWatts, target.TargetWatts * PowerTolerance);
            for (var sampleIndex = 1; sampleIndex < session.Samples.Length; sampleIndex++)
            {
                var previous = session.Samples[sampleIndex - 1];
                var current = session.Samples[sampleIndex];
                var intervalStart = previous.CapturedAt > scoredStart ? previous.CapturedAt : scoredStart;
                var intervalEnd = current.CapturedAt < end ? current.CapturedAt : end;
                var elapsed = intervalEnd - intervalStart;
                if (elapsed <= TimeSpan.Zero || current.CapturedAt - previous.CapturedAt > maximumGap)
                {
                    continue;
                }

                segmentTicks += elapsed.Ticks;
                if (scorePower && previous.PowerWatts is double watts)
                {
                    segmentPower += elapsed.Ticks;
                    if (double.Abs(watts - target.TargetWatts) <= tolerance)
                    {
                        segmentPowerHit += elapsed.Ticks;
                    }
                }

                if (scoreHeartRate && previous.HeartRateBeatsPerMinute is double bpm and > 0d)
                {
                    segmentHeartRate += elapsed.Ticks;
                    if ((target.MinimumHeartRate is not ushort minimum || bpm >= minimum)
                        && (target.MaximumHeartRate is not ushort maximum || bpm <= maximum))
                    {
                        segmentHeartRateHit += elapsed.Ticks;
                    }
                }
            }

            if (segmentPower == 0 && segmentHeartRate == 0)
            {
                continue;
            }

            powerTicks += segmentPower;
            powerOnTargetTicks += segmentPowerHit;
            heartRateTicks += segmentHeartRate;
            heartRateInRangeTicks += segmentHeartRateHit;
            segments.Add(new(
                target.SegmentIndex,
                target.Name,
                FormatTarget(target),
                TimeSpan.FromTicks(segmentTicks),
                Fraction(segmentPowerHit, segmentPower),
                Fraction(segmentHeartRateHit, segmentHeartRate)));
        }

        return segments.Count == 0
            ? null
            : new(segments, Fraction(powerOnTargetTicks, powerTicks), Fraction(heartRateInRangeTicks, heartRateTicks));
    }

    public static RideChartSeries GetChartSeries(RecordedSession session, int maximumPoints = 600)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPoints, 2);
        var samples = session.Samples;
        if (samples.Length == 0)
        {
            return RideChartSeries.Empty;
        }

        var start = samples[0].CapturedAt;
        var end = samples[^1].CapturedAt;
        var bucket = TimeSpan.FromTicks(long.Max(TimeSpan.TicksPerSecond, (end - start).Ticks / maximumPoints + 1));
        var power = new List<TelemetrySample>();
        var heartRate = new List<TelemetrySample>();
        var cadence = new List<TelemetrySample>();
        var index = 0;
        while (index < samples.Length)
        {
            var bucketStart = start + TimeSpan.FromTicks((samples[index].CapturedAt - start).Ticks / bucket.Ticks * bucket.Ticks);
            var bucketEnd = bucketStart + bucket;
            double powerSum = 0, heartRateSum = 0, cadenceSum = 0;
            int powerCount = 0, heartRateCount = 0, cadenceCount = 0;
            for (; index < samples.Length && samples[index].CapturedAt < bucketEnd; index++)
            {
                var sample = samples[index];
                Accumulate(sample.PowerWatts, ref powerSum, ref powerCount);
                Accumulate(sample.HeartRateBeatsPerMinute is > 0d ? sample.HeartRateBeatsPerMinute : null, ref heartRateSum, ref heartRateCount);
                Accumulate(sample.CadenceRpm, ref cadenceSum, ref cadenceCount);
            }

            Append(power, bucketStart, powerSum, powerCount);
            Append(heartRate, bucketStart, heartRateSum, heartRateCount);
            Append(cadence, bucketStart, cadenceSum, cadenceCount);
        }

        var targets = new List<TelemetrySample>();
        if (session.Context?.Targets is { Length: > 0 } segments)
        {
            var ordered = segments.OrderBy(static target => target.StartedAt).ToArray();
            for (var segmentIndex = 0; segmentIndex < ordered.Length; segmentIndex++)
            {
                var target = ordered[segmentIndex];
                if (target.IsHeartRateLed || target.TargetWatts <= 0)
                {
                    continue;
                }

                targets.Add(new(target.StartedAt, target.TargetWatts));
                targets.Add(new(GetSegmentEnd(session, ordered, segmentIndex), target.TargetWatts));
            }
        }

        return new(power, heartRate, cadence, targets, start, end);
    }

    private static IReadOnlyList<RideZoneShare> GetZoneTimes(
        IReadOnlyList<RecordedTelemetrySample> samples,
        IReadOnlyList<(string Code, string Label)> zones,
        Func<RecordedTelemetrySample, int?> zoneIndex,
        TimeSpan maximumGap)
    {
        var ticks = new long[zones.Count];
        for (var index = 1; index < samples.Count; index++)
        {
            var previous = samples[index - 1];
            var elapsed = samples[index].CapturedAt - previous.CapturedAt;
            if (elapsed > TimeSpan.Zero && elapsed <= maximumGap && zoneIndex(previous) is int zone and >= 0 && zone < ticks.Length)
            {
                ticks[zone] += elapsed.Ticks;
            }
        }

        var total = ticks.Sum();
        return total == 0
            ? []
            : [.. zones.Select((zone, index) => new RideZoneShare(zone.Code, zone.Label, TimeSpan.FromTicks(ticks[index]), ticks[index] / (double)total))];
    }

    private static int? FindHeartRateZone(IReadOnlyList<RideHeartRateZone> zones, double beatsPerMinute)
    {
        for (var index = zones.Count - 1; index >= 0; index--)
        {
            if (beatsPerMinute >= zones[index].MinimumBeatsPerMinute)
            {
                return index;
            }
        }

        return null;
    }

    private static int IndexOf(IReadOnlyList<PowerZone> zones, PowerZone zone)
    {
        for (var index = 0; index < zones.Count; index++)
        {
            if (ReferenceEquals(zones[index], zone))
            {
                return index;
            }
        }

        return -1;
    }

    private static DateTimeOffset GetSegmentEnd(RecordedSession session, IReadOnlyList<RideTargetSegment> ordered, int index)
    {
        var start = ordered[index].StartedAt;
        var end = index + 1 < ordered.Count ? ordered[index + 1].StartedAt : DateTimeOffset.MaxValue;
        foreach (var marker in session.Events)
        {
            if (marker.Type is SessionEventType.Stop or SessionEventType.Disconnect && marker.Timestamp > start && marker.Timestamp < end)
            {
                end = marker.Timestamp;
            }
        }

        return end != DateTimeOffset.MaxValue
            ? end
            : session.Samples.Length > 0 ? session.Samples[^1].CapturedAt : start;
    }

    private static string FormatTarget(RideTargetSegment target)
    {
        var heartRate = (target.MinimumHeartRate, target.MaximumHeartRate) switch
        {
            (ushort minimum, ushort maximum) => string.Create(CultureInfo.InvariantCulture, $"HR {minimum}–{maximum} bpm"),
            (null, ushort maximum) => string.Create(CultureInfo.InvariantCulture, $"HR ≤{maximum} bpm"),
            (ushort minimum, null) => string.Create(CultureInfo.InvariantCulture, $"HR ≥{minimum} bpm"),
            _ => null,
        };
        var power = target.IsHeartRateLed || target.TargetWatts <= 0
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{target.TargetWatts} W");
        return string.Join(" · ", new[] { power, heartRate }.OfType<string>());
    }

    private static string FormatHeartRateRange(RideHeartRateZone zone) => zone.MaximumBeatsPerMinute is ushort maximum
        ? string.Create(CultureInfo.InvariantCulture, $"{zone.MinimumBeatsPerMinute}–{maximum} bpm")
        : string.Create(CultureInfo.InvariantCulture, $"≥{zone.MinimumBeatsPerMinute} bpm");

    private static string FormatPowerRange(PowerZone zone) => zone.MaximumWatts is int maximum
        ? string.Create(CultureInfo.InvariantCulture, $"{zone.MinimumWatts}–{maximum} W")
        : string.Create(CultureInfo.InvariantCulture, $"≥{zone.MinimumWatts} W");

    private static double? Fraction(long part, long total) => total > 0 ? part / (double)total : null;

    private static void Accumulate(double? value, ref double sum, ref int count)
    {
        if (value is double number)
        {
            sum += number;
            count++;
        }
    }

    private static void Append(List<TelemetrySample> samples, DateTimeOffset timestamp, double sum, int count)
    {
        if (count > 0)
        {
            samples.Add(new(timestamp, sum / count));
        }
    }
}