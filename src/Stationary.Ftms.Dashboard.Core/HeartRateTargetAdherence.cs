namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct HeartRateTargetAdherence(TimeSpan InRange, TimeSpan Observed)
{
    public double? Percentage => Observed > TimeSpan.Zero
        ? InRange / Observed * 100d
        : null;
}

public static class HeartRateTargetAdherenceCalculator
{
    public static HeartRateTargetAdherence Calculate(
        IReadOnlyList<RecordedTelemetrySample> samples,
        ushort minimumBeatsPerMinute,
        ushort maximumBeatsPerMinute,
        DateTimeOffset start,
        DateTimeOffset end,
        TimeSpan maximumSampleGap)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (maximumBeatsPerMinute < minimumBeatsPerMinute
            || end < start
            || maximumSampleGap <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBeatsPerMinute));
        }

        var inRangeTicks = 0L;
        var observedTicks = 0L;
        for (var index = 1; index < samples.Count; index++)
        {
            var previous = samples[index - 1];
            var current = samples[index];
            var intervalStart = previous.CapturedAt > start ? previous.CapturedAt : start;
            var intervalEnd = current.CapturedAt < end ? current.CapturedAt : end;
            var elapsed = intervalEnd - intervalStart;
            if (elapsed <= TimeSpan.Zero
                || elapsed > maximumSampleGap
                || previous.HeartRateBeatsPerMinute is not double previousHeartRate
                || previousHeartRate <= 0d)
            {
                continue;
            }

            observedTicks += elapsed.Ticks;
            if (previousHeartRate >= minimumBeatsPerMinute && previousHeartRate <= maximumBeatsPerMinute)
            {
                inRangeTicks += elapsed.Ticks;
            }
        }

        return new(TimeSpan.FromTicks(inRangeTicks), TimeSpan.FromTicks(observedTicks));
    }
}