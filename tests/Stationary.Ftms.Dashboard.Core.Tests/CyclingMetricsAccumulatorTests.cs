using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class CyclingMetricsAccumulatorTests
{
    [Test]
    public async Task Add_ConstantPowerProducesExpectedMetrics()
    {
        var accumulator = new CyclingMetricsAccumulator(TimeSpan.FromSeconds(5));
        var start = DateTimeOffset.UnixEpoch;
        for (var second = 0; second <= 60; second++)
        {
            accumulator.Add(new(start.AddSeconds(second), 200d));
        }

        var metrics = accumulator.GetMetrics(200);

        using (Assert.Multiple())
        {
            await Assert.That(metrics.TotalWorkKilojoules).IsEqualTo(12d);
            await Assert.That(metrics.PeakPower5Seconds).IsEqualTo(200d);
            await Assert.That(metrics.PeakPower30Seconds).IsEqualTo(200d);
            await Assert.That(metrics.PeakPower1Minute).IsEqualTo(200d);
            await Assert.That(metrics.PeakPower5Minutes).IsNull();
            await Assert.That(metrics.NormalizedPower).IsEqualTo(200d);
            await Assert.That(metrics.IntensityFactor).IsEqualTo(1d);
            await Assert.That(metrics.TrainingStressScore).IsEqualTo(100d / 60d);
        }
    }

    [Test]
    public async Task Add_DoesNotIntegrateAcrossTelemetryGap()
    {
        var accumulator = new CyclingMetricsAccumulator(TimeSpan.FromSeconds(2));
        var start = DateTimeOffset.UnixEpoch;

        accumulator.Add(new(start, 100d));
        accumulator.Add(new(start.AddSeconds(1), 100d));
        accumulator.Add(new(start.AddSeconds(10), 300d));
        accumulator.Add(new(start.AddSeconds(11), 300d));

        var metrics = accumulator.GetMetrics();

        using (Assert.Multiple())
        {
            await Assert.That(metrics.TotalWorkKilojoules).IsEqualTo(0.4d);
            await Assert.That(metrics.NormalizedPower).IsNull();
            await Assert.That(metrics.IntensityFactor).IsNull();
            await Assert.That(metrics.TrainingStressScore).IsNull();
        }
    }
}