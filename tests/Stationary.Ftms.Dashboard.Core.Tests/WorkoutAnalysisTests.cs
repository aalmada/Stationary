using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class WorkoutAnalysisTests
{
    [Test]
    public async Task Calculate_ReportsEfficiencyDecouplingAndCadenceDistribution()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = Enumerable.Range(0, 1_201)
            .Select(second => new RecordedTelemetrySample(
                start.AddSeconds(second),
                200d,
                30d,
                second < 600 ? 80d : 90d,
                second < 600 ? 120d : 130d))
            .ToArray();

        var metrics = WorkoutAnalysis.Calculate(samples);

        using (Assert.Multiple())
        {
            await Assert.That(metrics.PowerHeartRateEfficiency).IsNotNull();
            await Assert.That(metrics.PowerHeartRateEfficiency.GetValueOrDefault()).IsBetween(1.59d, 1.61d);
            await Assert.That(metrics.AerobicDecouplingPercentage).IsNotNull();
            await Assert.That(metrics.AerobicDecouplingPercentage.GetValueOrDefault()).IsBetween(7.6d, 7.8d);
            await Assert.That(metrics.AverageCadenceRpm).IsNotNull();
            await Assert.That(metrics.AverageCadenceRpm.GetValueOrDefault()).IsBetween(84.9d, 85.1d);
            await Assert.That(metrics.PreferredCadenceMinimumRpm).IsEqualTo(80d);
            await Assert.That(metrics.PreferredCadenceMaximumRpm).IsEqualTo(90d);
            await Assert.That(metrics.LowCadenceHighTorqueExposure).IsEqualTo(TimeSpan.Zero);
        }
    }

    [Test]
    public async Task Calculate_ReportsLowCadenceTorqueExposureAndRecovery()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = Enumerable.Range(0, 62)
            .Select(second => new RecordedTelemetrySample(
                start.AddSeconds(second),
                second == 0 ? 400d : 100d,
                20d,
                60d,
                second == 0 ? 160d : 160d - (20d * second / 61d)))
            .ToArray();

        var metrics = WorkoutAnalysis.Calculate(samples);

        using (Assert.Multiple())
        {
            await Assert.That(metrics.LowCadenceHighTorqueExposure).IsEqualTo(TimeSpan.FromSeconds(1));
            await Assert.That(metrics.HeartRateRecoveryBeatsPerMinute).IsNotNull();
            await Assert.That(metrics.HeartRateRecoveryBeatsPerMinute.GetValueOrDefault()).IsBetween(19d, 21d);
        }
    }
}