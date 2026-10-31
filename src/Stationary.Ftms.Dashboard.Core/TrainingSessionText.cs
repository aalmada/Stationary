using System.Globalization;

namespace Stationary.Ftms.Dashboard.Core;

public static class TrainingSessionText
{
    public const string CadenceAdherencePending = "Cadence in-range score appears after a work interval.";
    public const string HeartRateAdherencePending = "Heart-rate time-in-target score appears after a heart-rate interval.";

    public static readonly string WaitingForPedalling = string.Create(
        CultureInfo.InvariantCulture,
        $"Start pedalling. The workout begins at {TrainingCadenceGuard.PedallingCadenceRpm:F0} rpm.");

    public static readonly string Stalled = string.Create(
        CultureInfo.InvariantCulture,
        $"Cadence too low, resistance eased. Spin up to {TrainingCadenceGuard.PedallingCadenceRpm:F0} rpm to resume.");

    public static string Describe(RampFtpEstimate? estimate) => estimate switch
    {
        RampFtpEstimateReady ready => $"Estimated FTP: {ready.EstimatedFtpWatts} W. Review and accept to update zones.",
        RampFtpEstimateDeviceMaximumReached => "Ramp test was inconclusive because the bike reached its advertised power maximum.",
        RampFtpEstimateInsufficientTelemetry => "Ramp test was inconclusive because a continuous measured minute was not recorded.",
        _ => string.Empty,
    };
}