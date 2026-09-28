using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class RampFtpEstimatorTests
{
    [Test]
    public async Task Estimate_UsesMeasuredBestRollingMinute()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = Enumerable.Range(0, 61)
            .Select(second => new RecordedTelemetrySample(start.AddSeconds(second), 200d, 20d, 90d, 150d))
            .ToArray();

        var result = RampFtpEstimator.Estimate(samples, 400);

        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo(RampFtpEstimateStatus.Ready);
            await Assert.That(result.BestRollingMinutePowerWatts).IsEqualTo(200);
            await Assert.That(result.EstimatedFtpWatts).IsEqualTo((ushort)150);
        }
    }

    [Test]
    public async Task Estimate_ReportsAnInconclusiveResultWhenTheDeviceMaximumIsReached()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = Enumerable.Range(0, 61)
            .Select(second => new RecordedTelemetrySample(start.AddSeconds(second), 400d, 20d, 90d, 150d))
            .ToArray();

        var result = RampFtpEstimator.Estimate(samples, 400);

        await Assert.That(result.Status).IsEqualTo(RampFtpEstimateStatus.DeviceMaximumReached);
    }
}