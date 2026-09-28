namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct WorkoutAnalysisMetrics(
    double? PowerHeartRateEfficiency,
    double? AerobicDecouplingPercentage,
    double? AverageCadenceRpm,
    double? CadenceVariationPercentage,
    double? PreferredCadenceMinimumRpm,
    double? PreferredCadenceMaximumRpm,
    TimeSpan LowCadenceHighTorqueExposure,
    double? HeartRateRecoveryBeatsPerMinute);

public static class WorkoutAnalysis
{
    private static readonly TimeSpan MinimumDecouplingDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaximumSampleGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromMinutes(1);

    public static WorkoutAnalysisMetrics Calculate(IReadOnlyList<RecordedTelemetrySample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return default;
        }

        var paired = samples.Where(static sample => sample.PowerWatts is > 0d && sample.HeartRateBeatsPerMinute is > 0d).ToArray();
        var efficiency = GetEfficiency(paired);
        var decoupling = GetDecoupling(paired);
        var cadence = samples.Where(static sample => sample.CadenceRpm is > 0d).Select(static sample => sample.CadenceRpm!.Value).Order().ToArray();
        double? averageCadence = cadence.Length > 0 ? cadence.Average() : null;
        double? cadenceVariation = averageCadence is > 0d
            ? double.Sqrt(cadence.Average(value => double.Pow(value - averageCadence.Value, 2d))) / averageCadence.Value * 100d
            : null;

        return new(
            efficiency,
            decoupling,
            averageCadence,
            cadenceVariation,
            GetPercentile(cadence, 0.25d),
            GetPercentile(cadence, 0.75d),
            GetTorqueExposure(samples),
            GetRecovery(samples));
    }

    private static double? GetEfficiency(IReadOnlyList<RecordedTelemetrySample> samples) => samples.Count > 0
        ? samples.Average(static sample => sample.PowerWatts!.Value) / samples.Average(static sample => sample.HeartRateBeatsPerMinute!.Value)
        : null;

    private static double? GetDecoupling(IReadOnlyList<RecordedTelemetrySample> samples)
    {
        if (samples.Count < 2 || samples[^1].CapturedAt - samples[0].CapturedAt < MinimumDecouplingDuration)
        {
            return null;
        }

        var midpoint = samples[0].CapturedAt + TimeSpan.FromTicks((samples[^1].CapturedAt - samples[0].CapturedAt).Ticks / 2);
        var first = samples.Where(sample => sample.CapturedAt < midpoint).ToArray();
        var second = samples.Where(sample => sample.CapturedAt >= midpoint).ToArray();
        var firstEfficiency = GetEfficiency(first);
        var secondEfficiency = GetEfficiency(second);
        return firstEfficiency is > 0d && secondEfficiency is double ending
            ? (firstEfficiency.Value - ending) / firstEfficiency.Value * 100d
            : null;
    }

    private static TimeSpan GetTorqueExposure(IReadOnlyList<RecordedTelemetrySample> samples)
    {
        var ticks = 0L;
        for (var index = 1; index < samples.Count; index++)
        {
            var previous = samples[index - 1];
            var elapsed = samples[index].CapturedAt - previous.CapturedAt;
            if (elapsed <= TimeSpan.Zero
                || elapsed > MaximumSampleGap
                || previous.PowerWatts is not > 0d
                || previous.CadenceRpm is not double cadenceRpm
                || cadenceRpm is <= 0d or >= 70d)
            {
                continue;
            }

            var torque = previous.PowerWatts.Value * 30d / (double.Pi * cadenceRpm);
            if (torque >= 40d)
            {
                ticks += elapsed.Ticks;
            }
        }

        return TimeSpan.FromTicks(ticks);
    }

    private static double? GetRecovery(IReadOnlyList<RecordedTelemetrySample> samples)
    {
        double? bestRecovery = null;
        for (var index = 1; index < samples.Count; index++)
        {
            var previous = samples[index - 1];
            var current = samples[index];
            if (previous.PowerWatts is not > 0d
                || current.PowerWatts is not double currentPower
                || currentPower > previous.PowerWatts.Value * 0.6d
                || current.HeartRateBeatsPerMinute is not double startingHeartRate)
            {
                continue;
            }

            var recoveryEnd = current.CapturedAt + RecoveryWindow;
            for (var recoveryIndex = index + 1; recoveryIndex < samples.Count; recoveryIndex++)
            {
                var recoverySample = samples[recoveryIndex];
                if (recoverySample.CapturedAt > recoveryEnd)
                {
                    break;
                }

                if (recoverySample.HeartRateBeatsPerMinute is not double endingHeartRate)
                {
                    continue;
                }

                var elapsedMinutes = (recoverySample.CapturedAt - current.CapturedAt).TotalMinutes;
                if (elapsedMinutes <= 0d)
                {
                    continue;
                }

                var recovery = (startingHeartRate - endingHeartRate) / elapsedMinutes;
                bestRecovery = bestRecovery is double currentBest ? double.Max(currentBest, recovery) : recovery;
            }
        }

        return bestRecovery;
    }

    private static double? GetPercentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        var index = (int)double.Round((sortedValues.Count - 1) * percentile, MidpointRounding.AwayFromZero);
        return sortedValues[index];
    }
}