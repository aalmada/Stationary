namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct TrainingAdherenceSample(DateTimeOffset CapturedAt, double? PowerWatts);

public readonly record struct TrainingAdherenceResult(
    int EligibleSamples,
    int InTargetSamples,
    double? Percentage,
    int Stars)
{
    public static readonly TrainingAdherenceResult Empty = new(0, 0, null, 0);
}

public static class TrainingAdherence
{
    private static readonly TimeSpan SettlingDuration = TimeSpan.FromSeconds(5);

    public static TrainingAdherenceResult Calculate(
        IReadOnlyList<TrainingAdherenceSample> samples,
        int targetWatts,
        int deviceIncrementWatts,
        DateTimeOffset segmentStartedAt,
        DateTimeOffset segmentEndedAt,
        bool completed)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (targetWatts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWatts));
        }

        if (deviceIncrementWatts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceIncrementWatts));
        }

        var eligibleAfter = segmentStartedAt + SettlingDuration;
        var tolerance = int.Max(deviceIncrementWatts, int.Max(5, (int)double.Ceiling(targetWatts * 0.05d)));
        var eligible = 0;
        var inTarget = 0;
        foreach (var sample in samples)
        {
            if (sample.CapturedAt < eligibleAfter || sample.CapturedAt > segmentEndedAt || sample.PowerWatts is not double power)
            {
                continue;
            }

            eligible++;
            if (double.Abs(power - targetWatts) <= tolerance)
            {
                inTarget++;
            }
        }

        if (eligible == 0)
        {
            return TrainingAdherenceResult.Empty;
        }

        var percentage = inTarget * 100d / eligible;
        return new(eligible, inTarget, percentage, GetStars(percentage, completed));
    }

    private static int GetStars(double percentage, bool completed) => !completed
        ? 0
        : percentage >= 95d
            ? 3
            : percentage >= 80d
                ? 2
                : 1;
}