namespace Stationary.Ftms.Dashboard.Core;

public sealed record RideHeartRateZone(string Code, string Name, ushort MinimumBeatsPerMinute, ushort? MaximumBeatsPerMinute);

// A workout segment as it was prescribed when it started. It ends when the next segment starts or the ride stops.
public sealed record RideTargetSegment(
    int SegmentIndex,
    DateTimeOffset StartedAt,
    string Name,
    int TargetWatts,
    ushort? MinimumHeartRate,
    ushort? MaximumHeartRate,
    bool IsHeartRateLed,
    bool IsRampStep);

// Settings in effect during the ride, so saved rides are analysed with the zones and FTP the rider actually used.
public sealed record RideContext(
    ushort? FunctionalThresholdPowerWatts,
    string? HeartRateZoneProfileName,
    RideHeartRateZone[]? HeartRateZones,
    RideTargetSegment[]? Targets)
{
    public static RideHeartRateZone[] FromProfile(HeartRateZoneProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return [.. profile.Zones.Select(static zone => new RideHeartRateZone(zone.Code, zone.Name, zone.MinimumBeatsPerMinute, zone.MaximumBeatsPerMinute))];
    }
}