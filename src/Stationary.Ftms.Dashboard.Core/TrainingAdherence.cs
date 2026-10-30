namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct TrainingAdherenceSample(DateTimeOffset CapturedAt, double? CadenceRpm);

public sealed record TrainingAdherenceScore(TimeSpan EligibleDuration, TimeSpan InTargetDuration, double Percentage, int Stars);

public sealed record TrainingAdherenceNoCadenceTarget;

public sealed record TrainingAdherenceNoEligibleCadence;

public union TrainingAdherenceResult(TrainingAdherenceScore, TrainingAdherenceNoCadenceTarget, TrainingAdherenceNoEligibleCadence);

public static class TrainingAdherence
{
    public static readonly TimeSpan TrainerSettlingDuration = TimeSpan.FromSeconds(10);

    // Riders took 7.5–12 s to reach a new cadence band while ERG traded resistance for cadence.
    public static readonly TimeSpan CadenceTransitionDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumMeasurementGap = TimeSpan.FromSeconds(5);

    public static bool ChangesCadenceTarget(TrainingSegment? previous, TrainingSegment current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return previous is not null
            && (current.MinimumCadenceRpm is not null || current.MaximumCadenceRpm is not null)
            && (previous.MinimumCadenceRpm != current.MinimumCadenceRpm || previous.MaximumCadenceRpm != current.MaximumCadenceRpm);
    }

    public static TimeSpan GetSettlingDuration(TrainingSegment? previous, TrainingSegment current) =>
        ChangesCadenceTarget(previous, current) ? CadenceTransitionDuration : TrainerSettlingDuration;

    public static TrainingAdherenceResult Calculate(
        IReadOnlyList<TrainingAdherenceSample> samples,
        int? minimumCadenceRpm,
        int? maximumCadenceRpm,
        DateTimeOffset segmentStartedAt,
        DateTimeOffset segmentEndedAt,
        bool completed,
        TimeSpan? settlingDuration = null)
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

        var eligibleAfter = segmentStartedAt + (settlingDuration ?? TrainerSettlingDuration);
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
            return new TrainingAdherenceNoEligibleCadence();
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