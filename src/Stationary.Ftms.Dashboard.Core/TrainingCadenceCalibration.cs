namespace Stationary.Ftms.Dashboard.Core;

// Refines the rider's preferred cadence from the opening warm-up so later cadence ranges stay reachable.
public static class TrainingCadenceCalibration
{
    public const int MaximumAdjustmentRpm = 10;
    public const int MinimumSampleCount = 30;

    public static bool IsCalibrationSegment(CompiledTrainingWorkout workout, int segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(workout);
        return segmentIndex == 0
            && workout.Segments.Count > 1
            && workout.Definition.Segments.Count == workout.Segments.Count
            && workout.Segments[0].Definition is { CountsTowardAdherence: false, MinimumCadenceRpm: not null, MaximumCadenceRpm: not null };
    }

    public static int? Calibrate(IEnumerable<double> cadenceSamplesRpm, int preferredCadenceRpm) =>
        MeasureMedianRpm(cadenceSamplesRpm) is { } measured ? Limit(measured, preferredCadenceRpm) : null;

    public static int Limit(int measuredCadenceRpm, int preferredCadenceRpm) => int.Clamp(
        int.Clamp(measuredCadenceRpm, preferredCadenceRpm - MaximumAdjustmentRpm, preferredCadenceRpm + MaximumAdjustmentRpm),
        TrainingWorkoutCompiler.MinimumPreferredCadenceRpm,
        TrainingWorkoutCompiler.MaximumPreferredCadenceRpm);

    public static int? MeasureMedianRpm(IEnumerable<double> cadenceSamplesRpm)
    {
        ArgumentNullException.ThrowIfNull(cadenceSamplesRpm);
        var samples = cadenceSamplesRpm
            .Where(static cadence => double.IsFinite(cadence) && cadence > TrainingCadenceGuard.StallCadenceRpm)
            .Order()
            .ToArray();
        if (samples.Length < MinimumSampleCount)
        {
            return null;
        }

        var middle = samples.Length / 2;
        var median = samples.Length % 2 == 0 ? (samples[middle - 1] + samples[middle]) / 2d : samples[middle];
        return (int)double.Round(median, MidpointRounding.AwayFromZero);
    }
}