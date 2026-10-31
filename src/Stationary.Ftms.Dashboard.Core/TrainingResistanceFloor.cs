namespace Stationary.Ftms.Dashboard.Core;

// ERG trainers have a lightest resistance level, so at a given cadence power cannot fall below it.
// The floor is learned when power stays well above a settled target; afterwards any lower settled ratio lowers it.
public readonly record struct TrainingResistanceFloor(double? WattsPerRpm, DateTimeOffset? AboveTargetSince, double? WindowMinimumWattsPerRpm)
{
    // Some trainers switch to lighter resistance levels at low cadence, so the floor is only learned and applied above this.
    public const double MinimumCadenceRpm = 70d;
    public const double AboveTargetTolerance = 1.1d;
    public static readonly TimeSpan PersistenceDuration = TimeSpan.FromSeconds(10);

    // Power and cadence are reported in whole units, so the ratio jitters by about 1% between samples at the same level.
    public const double MinimumImprovement = 0.99d;

    // Changed is true when the estimate changes.
    public (TrainingResistanceFloor Floor, bool Changed) Observe(DateTimeOffset timestamp, double? powerWatts, double? cadenceRpm, int targetWatts, bool settled)
    {
        if (!settled
            || powerWatts is not double power
            || cadenceRpm is not double cadence
            || cadence < MinimumCadenceRpm
            || power <= 0d)
        {
            return (ResetWindow(), false);
        }

        var ratio = power / cadence;
        if (WattsPerRpm is double floor)
        {
            return ratio >= floor * MinimumImprovement
                ? (this, false)
                : (this with { WattsPerRpm = ratio }, true);
        }

        if (power <= targetWatts * AboveTargetTolerance)
        {
            return (ResetWindow(), false);
        }

        var since = AboveTargetSince ?? timestamp;
        var windowMinimum = double.Min(WindowMinimumWattsPerRpm ?? double.PositiveInfinity, ratio);
        return timestamp - since < PersistenceDuration
            ? (this with { AboveTargetSince = since, WindowMinimumWattsPerRpm = windowMinimum }, false)
            : (new(windowMinimum, null, null), true);
    }

    public int? EstimateMinimumWatts(double cadenceRpm) =>
        WattsPerRpm is double floor && cadenceRpm >= MinimumCadenceRpm
            ? (int)double.Round(floor * cadenceRpm)
            : null;

    public bool IsAboveTarget(int targetWatts, double cadenceRpm) =>
        EstimateMinimumWatts(cadenceRpm) is int minimum && minimum > targetWatts * AboveTargetTolerance;

    // The lowest cadence, not below the band minimum, at which the floor still lets power approach the target.
    public int? GetClosestCadence(int targetWatts, int? minimumCadenceRpm) =>
        WattsPerRpm is double floor
            ? (int)double.Ceiling(double.Max(targetWatts / floor, double.Max(minimumCadenceRpm ?? MinimumCadenceRpm, MinimumCadenceRpm)))
            : null;

    private TrainingResistanceFloor ResetWindow() => this with { AboveTargetSince = null, WindowMinimumWattsPerRpm = null };
}