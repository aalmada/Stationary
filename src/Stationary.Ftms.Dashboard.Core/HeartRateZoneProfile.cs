namespace Stationary.Ftms.Dashboard.Core;

public enum HeartRateZoneMethod
{
    CyclingLactateThreshold,
    HeartRateReserve,
    MaximumHeartRate,
}

public sealed class HeartRateZoneProfile
{
    private readonly HeartRateZone[] zones;

    public HeartRateZoneProfile(string name, IEnumerable<HeartRateZone> zones)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(zones);

        this.zones = [.. zones];
        if (this.zones.Length == 0)
        {
            throw new ArgumentException("At least one heart-rate zone is required.", nameof(zones));
        }

        HashSet<string> codes = [with(StringComparer.OrdinalIgnoreCase)];
        ushort expectedMinimum = 0;
        for (var index = 0; index < this.zones.Length; index++)
        {
            var zone = this.zones[index] ?? throw new ArgumentException("Heart-rate zones cannot contain null values.", nameof(zones));
            if (!codes.Add(zone.Code))
            {
                throw new ArgumentException("Heart-rate zone codes must be unique.", nameof(zones));
            }

            if (zone.MinimumBeatsPerMinute != expectedMinimum)
            {
                throw new ArgumentException("Heart-rate zones must be contiguous and begin at 0 bpm.", nameof(zones));
            }

            if (index == this.zones.Length - 1)
            {
                if (zone.MaximumBeatsPerMinute is not null)
                {
                    throw new ArgumentException("The final heart-rate zone must not have an upper bound.", nameof(zones));
                }

                continue;
            }

            if (zone.MaximumBeatsPerMinute is not ushort maximum || maximum == ushort.MaxValue)
            {
                throw new ArgumentException("Only the final heart-rate zone may be unbounded.", nameof(zones));
            }

            expectedMinimum = (ushort)(maximum + 1);
        }

        Name = name;
    }

    public string Name { get; }

    public IReadOnlyList<HeartRateZone> Zones => zones;

    public HeartRateZone GetZone(ushort beatsPerMinute) => zones[GetZoneIndex(beatsPerMinute)];

    public int GetZoneIndex(ushort beatsPerMinute)
    {
        for (var index = 0; index < zones.Length; index++)
        {
            if (zones[index].Contains(beatsPerMinute))
            {
                return index;
            }
        }

        throw new InvalidOperationException("The heart-rate zone profile does not cover the supplied heart rate.");
    }

    public static HeartRateZoneProfile CreateCyclingLactateThreshold(ushort lactateThresholdBeatsPerMinute)
    {
        if (lactateThresholdBeatsPerMinute is < 50 or > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(lactateThresholdBeatsPerMinute));
        }

        var zone2Minimum = GetMinimum(lactateThresholdBeatsPerMinute, 81);
        var zone3Minimum = GetMinimum(lactateThresholdBeatsPerMinute, 90);
        var zone4Minimum = GetMinimum(lactateThresholdBeatsPerMinute, 94);
        var zone5aMinimum = GetMinimum(lactateThresholdBeatsPerMinute, 100);
        var zone5bMinimum = GetMinimum(lactateThresholdBeatsPerMinute, 103);
        var zone5cMinimum = GetExclusiveMinimum(lactateThresholdBeatsPerMinute, 106);

        return new HeartRateZoneProfile("Cycling LTHR", [
            new("Z1", "Recovery", 0, (ushort)(zone2Minimum - 1)),
            new("Z2", "Endurance", zone2Minimum, (ushort)(zone3Minimum - 1)),
            new("Z3", "Tempo", zone3Minimum, (ushort)(zone4Minimum - 1)),
            new("Z4", "Threshold", zone4Minimum, (ushort)(zone5aMinimum - 1)),
            new("Z5a", "Aerobic capacity", zone5aMinimum, (ushort)(zone5bMinimum - 1)),
            new("Z5b", "Anaerobic capacity", zone5bMinimum, (ushort)(zone5cMinimum - 1)),
            new("Z5c", "Sprint", zone5cMinimum, null),
        ]);
    }

    public static HeartRateZoneProfile CreateHeartRateReserve(ushort maximumBeatsPerMinute, ushort restingBeatsPerMinute)
    {
        if (maximumBeatsPerMinute is < 80 or > 250)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBeatsPerMinute));
        }

        if (restingBeatsPerMinute is < 30 or > 200
            || maximumBeatsPerMinute - restingBeatsPerMinute < 20)
        {
            throw new ArgumentOutOfRangeException(nameof(restingBeatsPerMinute));
        }

        return CreatePercentageProfile(
            "Heart-rate reserve",
            maximumBeatsPerMinute,
            restingBeatsPerMinute,
            [40, 60, 70, 90],
            ["Recovery", "Moderate", "Vigorous", "Hard", "Near maximum"]);
    }

    public static HeartRateZoneProfile CreateMaximumHeartRate(ushort maximumBeatsPerMinute)
    {
        if (maximumBeatsPerMinute is < 80 or > 250)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBeatsPerMinute));
        }

        return CreatePercentageProfile(
            "Maximum heart rate",
            maximumBeatsPerMinute,
            0,
            [60, 70, 80, 90],
            ["Recovery", "Endurance", "Tempo", "Threshold", "Near maximum"]);
    }

    public static ushort EstimateMaximumHeartRateFromAge(int ageYears)
    {
        if (ageYears is < 18 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(ageYears));
        }

        return checked((ushort)double.Round(208d - 0.7d * ageYears, MidpointRounding.AwayFromZero));
    }

    public static (ushort Minimum, ushort Maximum) GetHeartRateReserveRange(
        ushort maximumBeatsPerMinute,
        ushort restingBeatsPerMinute,
        int minimumReservePercentage,
        int maximumReservePercentage)
    {
        if (maximumBeatsPerMinute is < 80 or > 250)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBeatsPerMinute));
        }

        if (restingBeatsPerMinute is < 30 or > 200
            || maximumBeatsPerMinute - restingBeatsPerMinute < 20)
        {
            throw new ArgumentOutOfRangeException(nameof(restingBeatsPerMinute));
        }

        if (minimumReservePercentage is < 0 or > 100
            || maximumReservePercentage < minimumReservePercentage
            || maximumReservePercentage > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumReservePercentage));
        }

        var reserve = maximumBeatsPerMinute - restingBeatsPerMinute;
        var minimum = restingBeatsPerMinute + (int)double.Ceiling(reserve * minimumReservePercentage / 100d);
        var maximum = restingBeatsPerMinute + (int)double.Floor(reserve * maximumReservePercentage / 100d);
        return (checked((ushort)minimum), checked((ushort)maximum));
    }

    private static ushort GetMinimum(ushort threshold, int percentage) => (ushort)((threshold * percentage + 99) / 100);

    private static ushort GetExclusiveMinimum(ushort threshold, int percentage) => (ushort)((threshold * percentage / 100) + 1);

    private static HeartRateZoneProfile CreatePercentageProfile(
        string name,
        ushort maximumHeartRate,
        ushort restingHeartRate,
        IReadOnlyList<int> boundaries,
        IReadOnlyList<string> names)
    {
        var reserve = maximumHeartRate - restingHeartRate;
        var zones = new HeartRateZone[names.Count];
        ushort minimum = 0;
        for (var index = 0; index < names.Count; index++)
        {
            ushort? maximum = index == names.Count - 1
                ? null
                : checked((ushort)(restingHeartRate
                    + (int)double.Ceiling(reserve * boundaries[index] / 100d)
                    - 1));
            zones[index] = new($"Z{index + 1}", names[index], minimum, maximum);
            if (maximum is ushort upper)
            {
                minimum = checked((ushort)(upper + 1));
            }
        }

        return new(name, zones);
    }
}