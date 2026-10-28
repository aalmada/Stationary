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
    public async Task Estimate_InterpolatesTheBoundaryForJitteredSamples()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = Enumerable.Range(0, 61)
            .Select(index => new RecordedTelemetrySample(start.AddMilliseconds(index * 1001), 200d, null, null, null))
            .ToArray();

        var result = RampFtpEstimator.Estimate(samples, 400);

        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo(RampFtpEstimateStatus.Ready);
            await Assert.That(result.BestRollingMinutePowerWatts).IsEqualTo(200);
        }
    }

    [Test]
    public async Task Estimate_WeightsUnevenSamplesByElapsedTime()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = Enumerable.Range(0, 11)
            .Select(index => new RecordedTelemetrySample(start.AddSeconds(index * 5), 100d, null, null, null))
            .Concat(Enumerable.Range(51, 10)
                .Select(second => new RecordedTelemetrySample(start.AddSeconds(second), 300d, null, null, null)))
            .ToArray();

        var result = RampFtpEstimator.Estimate(samples, 400);

        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo(RampFtpEstimateStatus.Ready);
            await Assert.That(result.BestRollingMinutePowerWatts).IsEqualTo(132);
            await Assert.That(result.EstimatedFtpWatts).IsEqualTo((ushort)99);
        }
    }

    [Test]
    public async Task Estimate_RejectsWindowsWithGapsOverFiveSeconds()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = Enumerable.Range(0, 21)
            .Concat(Enumerable.Range(26, 35))
            .Select(second => new RecordedTelemetrySample(start.AddSeconds(second), 200d, null, null, null))
            .ToArray();

        var result = RampFtpEstimator.Estimate(samples, 400);

        await Assert.That(result.Status).IsEqualTo(RampFtpEstimateStatus.InsufficientTelemetry);
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