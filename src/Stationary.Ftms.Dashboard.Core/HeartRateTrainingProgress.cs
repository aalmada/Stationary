namespace Stationary.Ftms.Dashboard.Core;

public sealed record HeartRateTrainingRide(DateTimeOffset CompletedAt, long ZoneOneAndTwoTicks);

public sealed record HeartRateTrainingProgressDocument(IReadOnlyList<HeartRateTrainingRide> Rides);

public static class HeartRateTrainingProgress
{
    public static TimeSpan CalculateZoneOneAndTwoDuration(
        IReadOnlyList<RecordedTelemetrySample> samples,
        HeartRateZoneProfile profile,
        DateTimeOffset start,
        DateTimeOffset end,
        TimeSpan maximumSampleGap)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Zones.Count < 2 || end < start || maximumSampleGap <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        var ticks = 0L;
        for (var index = 1; index < samples.Count; index++)
        {
            var previous = samples[index - 1];
            var current = samples[index];
            var intervalStart = previous.CapturedAt > start ? previous.CapturedAt : start;
            var intervalEnd = current.CapturedAt < end ? current.CapturedAt : end;
            var elapsed = intervalEnd - intervalStart;
            if (elapsed <= TimeSpan.Zero
                || elapsed > maximumSampleGap
                || previous.HeartRateBeatsPerMinute is not double heartRate
                || heartRate is <= 0d or > ushort.MaxValue
                || profile.GetZoneIndex((ushort)heartRate) >= 2)
            {
                continue;
            }

            ticks += elapsed.Ticks;
        }

        return TimeSpan.FromTicks(ticks);
    }

    public static int GetMinutesWithinLastSevenDays(
        IReadOnlyList<HeartRateTrainingRide> rides,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rides);
        var threshold = now - TimeSpan.FromDays(7);
        var totalTicks = 0L;
        foreach (var ride in rides)
        {
            if (ride.CompletedAt >= threshold && ride.CompletedAt <= now && ride.ZoneOneAndTwoTicks > 0)
            {
                totalTicks = checked(totalTicks + ride.ZoneOneAndTwoTicks);
            }
        }

        return checked((int)(totalTicks / TimeSpan.TicksPerMinute));
    }
}