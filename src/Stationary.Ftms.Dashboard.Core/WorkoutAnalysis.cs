namespace Stationary.Ftms.Dashboard.Core;

public enum DecouplingAvailability
{
    NoData,
    TooShort,
    Variable,
    Available,
}

// PowerHeartRateEfficiency is the efficiency factor: normalized power divided by average heart rate over paired samples.
// HeartRateRecoveryBeatsPerMinute is the largest heart-rate drop measured 60 seconds after an effort ends.
public readonly record struct WorkoutAnalysisMetrics(
    double? PowerHeartRateEfficiency,
    double? AerobicDecouplingPercentage,
    double? AverageCadenceRpm,
    double? CadenceVariationPercentage,
    double? PreferredCadenceMinimumRpm,
    double? PreferredCadenceMaximumRpm,
    TimeSpan LowCadenceHighTorqueExposure,
    double? HeartRateRecoveryBeatsPerMinute,
    double? VariabilityIndex = null,
    DecouplingAvailability DecouplingAvailability = DecouplingAvailability.NoData);

public static class WorkoutAnalysis
{
    // Heart-rate drift only means something when power was steady; intervals and ramps change heart rate by design.
    public const double MaximumSteadyVariabilityIndex = 1.05d;

    private static readonly TimeSpan MinimumDecouplingDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaximumSampleGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RecoveryTolerance = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EffortWindow = TimeSpan.FromSeconds(30);

    public static WorkoutAnalysisMetrics Calculate(IReadOnlyList<RecordedTelemetrySample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return default;
        }

        var paired = samples.Where(static sample => sample.PowerWatts is > 0d && sample.HeartRateBeatsPerMinute is > 0d).ToArray();
        var efficiency = GetEfficiencyFactor(paired);
        var variabilityIndex = GetVariabilityIndex(samples);
        var (decoupling, availability) = GetDecoupling(paired, variabilityIndex);
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
            GetRecovery(samples),
            variabilityIndex,
            availability);
    }

    // Normalized power: fourth-power mean of the 30-second rolling average power.
    public static double? GetNormalizedPower(IReadOnlyList<RecordedTelemetrySample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var windowStart = 0;
        var windowSum = 0d;
        var windowCount = 0;
        var fourthPowerSum = 0d;
        var rollingCount = 0;
        DateTimeOffset? first = null;
        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            if (sample.PowerWatts is not double watts)
            {
                continue;
            }

            first ??= sample.CapturedAt;
            windowSum += watts;
            windowCount++;
            while (samples[windowStart].CapturedAt <= sample.CapturedAt - EffortWindow)
            {
                if (samples[windowStart].PowerWatts is double expired)
                {
                    windowSum -= expired;
                    windowCount--;
                }

                windowStart++;
            }

            if (sample.CapturedAt - first.Value >= EffortWindow - TimeSpan.FromSeconds(1) && windowCount > 0)
            {
                fourthPowerSum += double.Pow(double.Max(0d, windowSum / windowCount), 4d);
                rollingCount++;
            }
        }

        return rollingCount > 0 ? double.RootN(fourthPowerSum / rollingCount, 4) : null;
    }

    public static double? GetVariabilityIndex(double? normalizedPowerWatts, double averagePowerWatts) =>
        normalizedPowerWatts is double normalized && averagePowerWatts > 0d ? normalized / averagePowerWatts : null;

    private static double? GetVariabilityIndex(IReadOnlyList<RecordedTelemetrySample> samples)
    {
        var powers = samples.Select(static sample => sample.PowerWatts).OfType<double>().ToArray();
        return powers.Length > 0 ? GetVariabilityIndex(GetNormalizedPower(samples), powers.Average()) : null;
    }

    private static double? GetEfficiencyFactor(IReadOnlyList<RecordedTelemetrySample> samples) =>
        samples.Count > 0 && GetNormalizedPower(samples) is double normalized
            ? normalized / samples.Average(static sample => sample.HeartRateBeatsPerMinute!.Value)
            : GetEfficiency(samples);

    private static double? GetEfficiency(IReadOnlyList<RecordedTelemetrySample> samples) => samples.Count > 0
        ? samples.Average(static sample => sample.PowerWatts!.Value) / samples.Average(static sample => sample.HeartRateBeatsPerMinute!.Value)
        : null;

    private static (double? Value, DecouplingAvailability Availability) GetDecoupling(
        IReadOnlyList<RecordedTelemetrySample> samples,
        double? variabilityIndex)
    {
        if (samples.Count < 2)
        {
            return (null, DecouplingAvailability.NoData);
        }

        if (samples[^1].CapturedAt - samples[0].CapturedAt < MinimumDecouplingDuration)
        {
            return (null, DecouplingAvailability.TooShort);
        }

        if (variabilityIndex is not double index || index > MaximumSteadyVariabilityIndex)
        {
            return (null, DecouplingAvailability.Variable);
        }

        var midpoint = samples[0].CapturedAt + TimeSpan.FromTicks((samples[^1].CapturedAt - samples[0].CapturedAt).Ticks / 2);
        var first = samples.Where(sample => sample.CapturedAt < midpoint).ToArray();
        var second = samples.Where(sample => sample.CapturedAt >= midpoint).ToArray();
        var firstEfficiency = GetEfficiency(first);
        var secondEfficiency = GetEfficiency(second);
        return firstEfficiency is > 0d && secondEfficiency is double ending
            ? ((firstEfficiency.Value - ending) / firstEfficiency.Value * 100d, DecouplingAvailability.Available)
            : (null, DecouplingAvailability.NoData);
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

    // Heart-rate recovery: the drop in the 60 seconds after power falls below 60% of the preceding 30-second average.
    private static double? GetRecovery(IReadOnlyList<RecordedTelemetrySample> samples)
    {
        double? bestRecovery = null;
        DateTimeOffset? lastEffortEnd = null;
        for (var index = 1; index < samples.Count; index++)
        {
            var current = samples[index];
            if (current.PowerWatts is not double currentPower
                || current.HeartRateBeatsPerMinute is not double startingHeartRate
                || lastEffortEnd is { } previousEnd && current.CapturedAt - previousEnd < RecoveryWindow
                || GetPrecedingAveragePower(samples, index) is not double precedingPower
                || precedingPower <= 0d
                || currentPower > precedingPower * 0.6d
                || samples[index - 1].PowerWatts is double previousPower && previousPower <= precedingPower * 0.6d)
            {
                continue;
            }

            lastEffortEnd = current.CapturedAt;
            var recoveryEnd = current.CapturedAt + RecoveryWindow;
            for (var recoveryIndex = index + 1; recoveryIndex < samples.Count; recoveryIndex++)
            {
                var recoverySample = samples[recoveryIndex];
                if (recoverySample.CapturedAt - samples[recoveryIndex - 1].CapturedAt > MaximumSampleGap
                    || recoverySample.CapturedAt > recoveryEnd + RecoveryTolerance)
                {
                    break;
                }

                if (recoverySample.CapturedAt < recoveryEnd || recoverySample.HeartRateBeatsPerMinute is not double endingHeartRate)
                {
                    continue;
                }

                var recovery = startingHeartRate - endingHeartRate;
                bestRecovery = bestRecovery is double currentBest ? double.Max(currentBest, recovery) : recovery;
                break;
            }
        }

        return bestRecovery;
    }

    private static double? GetPrecedingAveragePower(IReadOnlyList<RecordedTelemetrySample> samples, int index)
    {
        var windowStart = samples[index].CapturedAt - EffortWindow;
        var sum = 0d;
        var count = 0;
        for (var previous = index - 1; previous >= 0 && samples[previous].CapturedAt >= windowStart; previous--)
        {
            if (samples[previous].PowerWatts is double watts)
            {
                sum += watts;
                count++;
            }
        }

        return count > 0 ? sum / count : null;
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