namespace Stationary.Ftms.Dashboard.Core;

public sealed record PowerZone(string Code, string Name, int MinimumWatts, int? MaximumWatts)
{
    public bool Contains(double watts) => watts >= MinimumWatts && (MaximumWatts is null || watts <= MaximumWatts);
}

public sealed class PowerZoneProfile
{
    private readonly PowerZone[] zones;

    private PowerZoneProfile(ushort functionalThresholdPowerWatts)
    {
        FunctionalThresholdPowerWatts = functionalThresholdPowerWatts;
        var zone2 = GetExclusiveMinimum(functionalThresholdPowerWatts, 55);
        var zone3 = GetExclusiveMinimum(functionalThresholdPowerWatts, 75);
        var zone4 = GetExclusiveMinimum(functionalThresholdPowerWatts, 90);
        var zone5 = GetExclusiveMinimum(functionalThresholdPowerWatts, 105);
        var zone6 = GetExclusiveMinimum(functionalThresholdPowerWatts, 120);
        var zone7 = GetExclusiveMinimum(functionalThresholdPowerWatts, 150);
        zones =
        [
            new("Z1", "Active recovery", 0, zone2 - 1),
            new("Z2", "Endurance", zone2, zone3 - 1),
            new("Z3", "Tempo", zone3, zone4 - 1),
            new("Z4", "Threshold", zone4, zone5 - 1),
            new("Z5", "VO2 max", zone5, zone6 - 1),
            new("Z6", "Anaerobic", zone6, zone7 - 1),
            new("Z7", "Neuromuscular", zone7, null),
        ];
    }

    public ushort FunctionalThresholdPowerWatts { get; }

    public IReadOnlyList<PowerZone> Zones => zones;

    public PowerZone GetZone(double watts)
    {
        var normalizedWatts = double.Max(0d, watts);
        return zones.First(zone => zone.Contains(normalizedWatts));
    }

    public static PowerZoneProfile Create(ushort functionalThresholdPowerWatts)
    {
        if (functionalThresholdPowerWatts == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(functionalThresholdPowerWatts));
        }

        return new(functionalThresholdPowerWatts);
    }

    private static int GetExclusiveMinimum(ushort threshold, int percentage) => (threshold * percentage / 100) + 1;
}