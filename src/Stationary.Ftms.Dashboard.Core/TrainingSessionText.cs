using System.Globalization;

namespace Stationary.Ftms.Dashboard.Core;

public static class TrainingSessionText
{
    public const string CadenceAdherencePending = "Cadence in-range score appears after a work interval.";
    public const string HeartRateAdherencePending = "Heart-rate time-in-target score appears after a heart-rate interval.";

    public static string TimeNearMaximum(ushort thresholdBeatsPerMinute, TimeSpan duration) => string.Create(
        CultureInfo.InvariantCulture,
        $"Time near maximum heart rate (at or above {thresholdBeatsPerMinute} bpm): {(int)duration.TotalMinutes}:{duration.Seconds:D2}");

    public static readonly string WaitingForPedalling = string.Create(
        CultureInfo.InvariantCulture,
        $"Start pedalling. The workout begins at {TrainingCadenceGuard.PedallingCadenceRpm:F0} rpm.");

    public static readonly string Stalled = string.Create(
        CultureInfo.InvariantCulture,
        $"Cadence too low, resistance eased. Spin up to {TrainingCadenceGuard.PedallingCadenceRpm:F0} rpm to resume.");

    // A change beyond this share of the current FTP is worth a second look before it rescales every workout.
    public const double LargeFtpChangePercentage = 15d;

    public static RampFtpReview? Review(RampFtpEstimate? estimate, ushort? currentFtpWatts) => estimate switch
    {
        RampFtpEstimateReady ready => new(
            $"Estimated FTP: {ready.EstimatedFtpWatts} W",
            string.Create(CultureInfo.InvariantCulture, $"Best 1-minute power {ready.BestRollingMinutePowerWatts} W x 75% = {ready.EstimatedFtpWatts} W. {DescribeChange(ready.EstimatedFtpWatts, currentFtpWatts)}"),
            CanAccept: true),
        RampFtpEstimateDeviceMaximumReached => new(
            "Ramp test inconclusive",
            "The bike reached its advertised maximum power before you did, so your limit was not found. Your FTP is unchanged.",
            CanAccept: false),
        RampFtpEstimateInsufficientTelemetry => new(
            "Ramp test inconclusive",
            "A continuous minute of power data was not recorded. Check the bike connection and retest; your FTP is unchanged.",
            CanAccept: false),
        _ => null,
    };

    private static string DescribeChange(ushort estimate, ushort? current)
    {
        if (current is not ushort previous)
        {
            return "Accept to set your first FTP and enable workout targets.";
        }

        var change = (estimate - previous) * 100d / previous;
        var comparison = string.Create(CultureInfo.InvariantCulture, $"Current FTP {previous} W ({change:+0;-0;0}%).");
        return double.Abs(change) > LargeFtpChangePercentage
            ? $"{comparison} That is a large change; retest if pacing or the power reading seemed off."
            : $"{comparison} Accept to rescale workout targets and power zones.";
    }

    public static string Describe(RampFtpEstimate? estimate) => estimate switch
    {
        RampFtpEstimateReady ready => $"Estimated FTP: {ready.EstimatedFtpWatts} W. Review it on Results.",
        RampFtpEstimateDeviceMaximumReached => "Ramp test was inconclusive because the bike reached its advertised power maximum.",
        RampFtpEstimateInsufficientTelemetry => "Ramp test was inconclusive because a continuous measured minute was not recorded.",
        _ => string.Empty,
    };
}