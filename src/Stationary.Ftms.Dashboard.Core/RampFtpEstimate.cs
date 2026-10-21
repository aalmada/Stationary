namespace Stationary.Ftms.Dashboard.Core;

public enum RampFtpEstimateStatus
{
    Ready,
    InsufficientTelemetry,
    DeviceMaximumReached,
}

public readonly record struct RampFtpEstimate(RampFtpEstimateStatus Status, int? BestRollingMinutePowerWatts, ushort? EstimatedFtpWatts)
{
    public bool IsReady => Status == RampFtpEstimateStatus.Ready;
}

public static class RampFtpEstimator
{
    private static readonly TimeSpan RollingWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaximumTelemetryGap = TimeSpan.FromSeconds(5);

    public static RampFtpEstimate Estimate(IReadOnlyList<RecordedTelemetrySample> samples, int deviceMaximumWatts)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (deviceMaximumWatts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceMaximumWatts));
        }

        var powerSamples = samples
            .Where(static sample => sample.PowerWatts is >= 0d)
            .OrderBy(static sample => sample.CapturedAt)
            .ToArray();
        if (powerSamples.Length < 2)
        {
            return new(RampFtpEstimateStatus.InsufficientTelemetry, null, null);
        }

        if (powerSamples.Any(sample => sample.PowerWatts >= deviceMaximumWatts))
        {
            return new(RampFtpEstimateStatus.DeviceMaximumReached, null, null);
        }

        double? bestAverage = null;
        for (var start = 0; start < powerSamples.Length; start++)
        {
            var windowStart = powerSamples[start].CapturedAt;
            var windowEnd = windowStart + RollingWindow;
            var window = new List<RecordedTelemetrySample>();
            var previous = powerSamples[start];
            for (var index = start; index < powerSamples.Length && powerSamples[index].CapturedAt <= windowEnd; index++)
            {
                var current = powerSamples[index];
                if (index > start && current.CapturedAt - previous.CapturedAt > MaximumTelemetryGap)
                {
                    break;
                }

                window.Add(current);
                previous = current;
            }

            if (window.Count < 2 || window[^1].CapturedAt - windowStart < RollingWindow)
            {
                continue;
            }

            var average = window.Average(static sample => sample.PowerWatts!.Value);
            bestAverage = bestAverage is double existing ? double.Max(existing, average) : average;
        }

        if (bestAverage is not double best)
        {
            return new(RampFtpEstimateStatus.InsufficientTelemetry, null, null);
        }

        var bestWatts = checked((int)double.Round(best, MidpointRounding.AwayFromZero));
        var estimate = checked((ushort)double.Round(best * 0.75d, MidpointRounding.AwayFromZero));
        return new(RampFtpEstimateStatus.Ready, bestWatts, estimate);
    }
}