namespace Stationary.Ftms.Dashboard.Core;

// ERG trainers have a lightest resistance level, so at a given cadence power cannot fall below it.
// The floor is learned when power stays well above a settled target; afterwards any lower settled ratio lowers it.
public sealed class TrainingResistanceFloor
{
    // Some trainers switch to lighter resistance levels at low cadence, so the floor is only learned and applied above this.
    public const double MinimumCadenceRpm = 70d;
    public const double AboveTargetTolerance = 1.1d;
    public static readonly TimeSpan PersistenceDuration = TimeSpan.FromSeconds(10);

    // Power and cadence are reported in whole units, so the ratio jitters by about 1% between samples at the same level.
    public const double MinimumImprovement = 0.99d;

    private DateTimeOffset? aboveTargetSince;
    private double windowMinimumWattsPerRpm = double.PositiveInfinity;

    public double? WattsPerRpm { get; private set; }

    // Returns true when the estimate changes.
    public bool Observe(DateTimeOffset timestamp, double? powerWatts, double? cadenceRpm, int targetWatts, bool settled)
    {
        if (!settled
            || powerWatts is not double power
            || cadenceRpm is not double cadence
            || cadence < MinimumCadenceRpm
            || power <= 0d)
        {
            ResetWindow();
            return false;
        }

        var ratio = power / cadence;
        if (WattsPerRpm is double floor)
        {
            if (ratio >= floor * MinimumImprovement)
            {
                return false;
            }

            WattsPerRpm = ratio;
            return true;
        }

        if (power <= targetWatts * AboveTargetTolerance)
        {
            ResetWindow();
            return false;
        }

        aboveTargetSince ??= timestamp;
        windowMinimumWattsPerRpm = double.Min(windowMinimumWattsPerRpm, ratio);
        if (timestamp - aboveTargetSince < PersistenceDuration)
        {
            return false;
        }

        WattsPerRpm = windowMinimumWattsPerRpm;
        ResetWindow();
        return true;
    }

    public int? EstimateMinimumWatts(double cadenceRpm) =>
        WattsPerRpm is double floor && cadenceRpm >= MinimumCadenceRpm
            ? (int)double.Round(floor * cadenceRpm)
            : null;

    public bool IsAboveTarget(int targetWatts, double cadenceRpm) =>
        EstimateMinimumWatts(cadenceRpm) is int minimum && minimum > targetWatts * AboveTargetTolerance;

    // The lowest cadence, not below the band minimum, at which the floor still lets power approach the target.
    public int? GetClosestCadence(int targetWatts, int? minimumCadenceRpm)
    {
        if (WattsPerRpm is not double floor)
        {
            return null;
        }

        var lowest = double.Max(minimumCadenceRpm ?? MinimumCadenceRpm, MinimumCadenceRpm);
        return (int)double.Ceiling(double.Max(targetWatts / floor, lowest));
    }

    public void Reset()
    {
        WattsPerRpm = null;
        ResetWindow();
    }

    private void ResetWindow()
    {
        aboveTargetSince = null;
        windowMinimumWattsPerRpm = double.PositiveInfinity;
    }
}