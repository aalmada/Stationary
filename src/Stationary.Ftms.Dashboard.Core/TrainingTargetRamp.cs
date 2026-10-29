namespace Stationary.Ftms.Dashboard.Core;

// Steps an ERG target toward its segment value so the trainer and rider do not fight a single large jump.
public readonly record struct TrainingTargetRamp(int FromWatts, int ToWatts, TimeSpan Duration)
{
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan StepInterval = TimeSpan.FromSeconds(3);

    public static TrainingTargetRamp Create(
        int? previousTargetWatts,
        CompiledTrainingSegment segment,
        TrainingPowerCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (!capabilities.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        }

        var target = segment.TargetWatts;
        var from = previousTargetWatts ?? Quantize(target / 2d, capabilities);
        var segmentLimit = segment.Definition.Duration / 4;
        var duration = segmentLimit < MaximumDuration ? segmentLimit : MaximumDuration;
        return int.Abs(target - from) <= capabilities.IncrementWatts || duration < StepInterval
            ? new(target, target, TimeSpan.Zero)
            : new(from, target, duration);
    }

    public int GetTargetWatts(TimeSpan elapsed, TrainingPowerCapabilities capabilities)
    {
        if (Duration <= TimeSpan.Zero || elapsed >= Duration)
        {
            return ToWatts;
        }

        var stepCount = (int)double.Ceiling(Duration / StepInterval);
        var stepIndex = elapsed <= TimeSpan.Zero ? 1 : (int)(elapsed / StepInterval) + 1;
        if (stepIndex >= stepCount)
        {
            return ToWatts;
        }

        var watts = FromWatts + ((ToWatts - FromWatts) * (double)stepIndex / stepCount);
        return Quantize(watts, capabilities);
    }

    private static int Quantize(double watts, TrainingPowerCapabilities capabilities)
    {
        var maximumStep = (capabilities.MaximumWatts - capabilities.MinimumWatts) / capabilities.IncrementWatts;
        var nearestStep = (int)double.Round(
            (watts - capabilities.MinimumWatts) / capabilities.IncrementWatts,
            MidpointRounding.AwayFromZero);
        return capabilities.MinimumWatts + (int.Clamp(nearestStep, 0, maximumStep) * capabilities.IncrementWatts);
    }
}