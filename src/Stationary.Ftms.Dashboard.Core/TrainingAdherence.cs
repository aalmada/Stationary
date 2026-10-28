namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct TrainingAdherenceSample(DateTimeOffset CapturedAt, double? CadenceRpm);

public sealed record TrainingAdherenceScore(TimeSpan EligibleDuration, TimeSpan InTargetDuration, double Percentage, int Stars);

public sealed record TrainingAdherenceNoCadenceTarget;

public sealed record TrainingAdherenceNoMeasuredCadence;

public union TrainingAdherenceResult(TrainingAdherenceScore, TrainingAdherenceNoCadenceTarget, TrainingAdherenceNoMeasuredCadence);

public static class TrainingAdherence
{
    private static readonly TimeSpan SettlingDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumMeasurementGap = TimeSpan.FromSeconds(5);

    public static TrainingAdherenceResult Calculate(
        IReadOnlyList<TrainingAdherenceSample> samples,
        int? minimumCadenceRpm,
        int? maximumCadenceRpm,
        DateTimeOffset segmentStartedAt,
        DateTimeOffset segmentEndedAt,
        bool completed)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (minimumCadenceRpm is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumCadenceRpm));
        }

        if (maximumCadenceRpm is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCadenceRpm));
        }

        if (minimumCadenceRpm is null && maximumCadenceRpm is null)
        {
            return new TrainingAdherenceNoCadenceTarget();
        }

        if (minimumCadenceRpm is int minimum && maximumCadenceRpm is int maximum && minimum > maximum)
        {
            throw new ArgumentException("The cadence range minimum cannot exceed its maximum.");
        }

        var eligibleAfter = segmentStartedAt + SettlingDuration;
        var eligibleTicks = 0L;
        var inTargetTicks = 0L;
        var orderedSamples = samples.OrderBy(static sample => sample.CapturedAt).ToArray();
        for (var index = 0; index < orderedSamples.Length; index++)
        {
            var sample = orderedSamples[index];
            if (sample.CapturedAt >= segmentEndedAt || sample.CadenceRpm is not double cadence)
            {
                continue;
            }

            var nextSampleAt = index + 1 < orderedSamples.Length
                ? orderedSamples[index + 1].CapturedAt
                : segmentEndedAt;
            var sampleEnd = nextSampleAt < segmentEndedAt ? nextSampleAt : segmentEndedAt;
            var maximumSampleEnd = sample.CapturedAt + MaximumMeasurementGap;
            if (maximumSampleEnd < sampleEnd)
            {
                sampleEnd = maximumSampleEnd;
            }

            var sampleStart = eligibleAfter > sample.CapturedAt ? eligibleAfter : sample.CapturedAt;
            if (sampleEnd <= sampleStart)
            {
                continue;
            }

            var durationTicks = (sampleEnd - sampleStart).Ticks;
            eligibleTicks += durationTicks;
            if ((minimumCadenceRpm is null || cadence >= minimumCadenceRpm)
                && (maximumCadenceRpm is null || cadence <= maximumCadenceRpm))
            {
                inTargetTicks += durationTicks;
            }
        }

        if (eligibleTicks == 0)
        {
            return new TrainingAdherenceNoMeasuredCadence();
        }

        var percentage = inTargetTicks * 100d / eligibleTicks;
        return new TrainingAdherenceScore(
            TimeSpan.FromTicks(eligibleTicks),
            TimeSpan.FromTicks(inTargetTicks),
            percentage,
            GetStars(percentage, completed));
    }

    private static int GetStars(double percentage, bool completed) => !completed
        ? 0
        : percentage >= 95d
            ? 3
            : percentage >= 80d
                ? 2
                : 1;
}