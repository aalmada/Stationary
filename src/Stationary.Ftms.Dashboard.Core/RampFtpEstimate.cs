namespace Stationary.Ftms.Dashboard.Core;

public sealed record RampFtpEstimateReady(int BestRollingMinutePowerWatts, ushort EstimatedFtpWatts);

public sealed record RampFtpEstimateInsufficientTelemetry;

public sealed record RampFtpEstimateDeviceMaximumReached;

public union RampFtpEstimate(RampFtpEstimateReady, RampFtpEstimateInsufficientTelemetry, RampFtpEstimateDeviceMaximumReached);

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
            return new RampFtpEstimateInsufficientTelemetry();
        }

        if (powerSamples.Any(sample => sample.PowerWatts >= deviceMaximumWatts))
        {
            return new RampFtpEstimateDeviceMaximumReached();
        }

        double? bestAverage = null;
        for (var start = 0; start < powerSamples.Length; start++)
        {
            var average = GetRollingMinuteAverage(powerSamples, start);
            if (average is double value)
            {
                bestAverage = bestAverage is double existing ? double.Max(existing, value) : value;
            }
        }

        if (bestAverage is not double best)
        {
            return new RampFtpEstimateInsufficientTelemetry();
        }

        var bestWatts = checked((int)double.Round(best, MidpointRounding.AwayFromZero));
        var estimate = checked((ushort)double.Round(best * 0.75d, MidpointRounding.AwayFromZero));
        return new RampFtpEstimateReady(bestWatts, estimate);
    }

    private static double? GetRollingMinuteAverage(IReadOnlyList<RecordedTelemetrySample> samples, int startIndex)
    {
        var windowStart = samples[startIndex].CapturedAt;
        var windowEnd = windowStart + RollingWindow;
        var previous = samples[startIndex];
        var integratedWattsSeconds = 0d;

        for (var index = startIndex + 1; index < samples.Count; index++)
        {
            var current = samples[index];
            var gap = current.CapturedAt - previous.CapturedAt;
            var segmentEnd = current.CapturedAt < windowEnd ? current.CapturedAt : windowEnd;
            var segmentDuration = segmentEnd - previous.CapturedAt;
            if (segmentDuration > MaximumTelemetryGap)
            {
                return null;
            }

            if (segmentEnd > previous.CapturedAt)
            {
                var previousPower = previous.PowerWatts!.Value;
                var currentPower = current.PowerWatts!.Value;
                var endRatio = segmentDuration.Ticks / (double)gap.Ticks;
                var endPower = previousPower + ((currentPower - previousPower) * endRatio);
                integratedWattsSeconds += (previousPower + endPower) * 0.5d * segmentDuration.TotalSeconds;
            }

            if (current.CapturedAt >= windowEnd)
            {
                return integratedWattsSeconds / RollingWindow.TotalSeconds;
            }

            previous = current;
        }

        return null;
    }
}