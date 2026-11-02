namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct RampHeartRateReview(string Text, ushort? SuggestedMaximumHeartRate);

public static class HeartRateZoneGuidance
{
    // Tab order of the zone-method picker.
    private static readonly HeartRateZoneMethod[] MethodOrder =
        [HeartRateZoneMethod.CyclingLactateThreshold, HeartRateZoneMethod.HeartRateReserve, HeartRateZoneMethod.MaximumHeartRate];

    public static IReadOnlyList<HeartRateZoneMethod> Methods => MethodOrder;

    public static IReadOnlyList<string> TabTitles { get; } = ["Cycling LTHR", "HR reserve", "Max HR"];

    public static int TabIndex(HeartRateZoneMethod method) => int.Max(0, Array.IndexOf(MethodOrder, method));

    public static string DisplayName(HeartRateZoneMethod method) => method switch
    {
        HeartRateZoneMethod.HeartRateReserve => "Heart-rate reserve",
        HeartRateZoneMethod.MaximumHeartRate => "Maximum heart rate",
        _ => "Cycling LTHR",
    };

    public static string Describe(HeartRateZoneMethod method) => method switch
    {
        HeartRateZoneMethod.HeartRateReserve => "Zones from max and resting heart rate (Karvonen). Needs Max HR and resting HR.",
        HeartRateZoneMethod.MaximumHeartRate => "Zones as a percentage of max heart rate. Needs Max HR only.",
        _ => "Zones from your cycling lactate-threshold heart rate, about your average over a hard 30-minute solo ride. Needs LTHR only.",
    };

    public static bool UsesLactateThreshold(HeartRateZoneMethod method) => method == HeartRateZoneMethod.CyclingLactateThreshold;

    public static bool UsesMaximumHeartRate(HeartRateZoneMethod method) => method is HeartRateZoneMethod.HeartRateReserve or HeartRateZoneMethod.MaximumHeartRate;

    public static bool UsesRestingHeartRate(HeartRateZoneMethod method) => method == HeartRateZoneMethod.HeartRateReserve;

    public static string DescribeStatus(HeartRateZoneProfile? profile, ushort? safetyCeiling)
    {
        if (profile is null)
        {
            return "Heart-rate zones not set. Open Heart rate to set them.";
        }

        var endurance = profile.Zones.FirstOrDefault(static zone => zone.Code == "Z2") ?? profile.Zones[0];
        var range = endurance.MaximumBeatsPerMinute is ushort maximum
            ? $"{endurance.MinimumBeatsPerMinute}-{maximum} bpm"
            : $"{endurance.MinimumBeatsPerMinute}+ bpm";
        var ceiling = safetyCeiling is ushort value ? $" · ceiling {value} bpm" : string.Empty;
        return $"{profile.Name} zones · {endurance.Code} {range}{ceiling}";
    }

    public static RampHeartRateReview ReviewRampPeak(ushort? peak, ushort? maximumHeartRate, ushort? lactateThreshold)
    {
        if (peak is not ushort beats)
        {
            return new("No heart rate was recorded during the ramp.", null);
        }

        var (maximumText, suggestion) = maximumHeartRate switch
        {
            null => (" No max HR is set; a ramp to exhaustion usually ends within a few beats of it.", (ushort?)beats),
            ushort maximum when beats > maximum => ($" That is above your max HR of {maximum} bpm.", beats),
            ushort maximum => ($" Your max HR is {maximum} bpm.", null),
        };
        var thresholdText = lactateThreshold is ushort threshold && beats <= threshold
            ? $" Your LTHR of {threshold} bpm is not below this peak, so it is probably too high. Retest it with a hard 30-minute ride."
            : string.Empty;
        return new($"Peak heart rate {beats} bpm.{maximumText}{thresholdText}", suggestion);
    }
}