namespace Stationary.Ftms.Dashboard.Core;

public static class TrainingCadenceGuidance
{
    public const string PedalFaster = "Pedal faster ↑";
    public const string PedalSlower = "Pedal slower ↓";
    public const string OnTarget = "Cadence on target ✓";

    public static string GetDirectionText(double? cadenceRpm, int? minimumCadenceRpm, int? maximumCadenceRpm) =>
        cadenceRpm is not double cadence || (minimumCadenceRpm is null && maximumCadenceRpm is null)
            ? string.Empty
            : minimumCadenceRpm is int minimum && cadence < minimum
                ? PedalFaster
                : maximumCadenceRpm is int maximum && cadence > maximum
                    ? PedalSlower
                    : OnTarget;
}