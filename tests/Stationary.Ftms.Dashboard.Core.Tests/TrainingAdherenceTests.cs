using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingAdherenceTests
{
    [Test]
    public async Task Calculate_ExcludesSettlingSamplesAndScoresCadenceRange()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(1), 70d),
            new TrainingAdherenceSample(start.AddSeconds(5), 80d),
            new TrainingAdherenceSample(start.AddSeconds(10), 82d),
            new TrainingAdherenceSample(start.AddSeconds(15), 91d),
        };

        var result = TrainingAdherence.Calculate(samples, 80, 90, start, start.AddSeconds(20), completed: true);

        await Assert.That(result is TrainingAdherenceScore
        { EligibleDuration: var eligible, InTargetDuration: var inTarget, Percentage: var percentage, Stars: 1 }
            && eligible == TimeSpan.FromSeconds(15)
            && inTarget == TimeSpan.FromSeconds(10)
            && double.Abs(percentage - (200d / 3d)) < 0.001d).IsTrue();
    }

    [Test]
    public async Task Calculate_SupportsCadenceTargetsWithOneOpenBoundary()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(5), 100d),
            new TrainingAdherenceSample(start.AddSeconds(10), 111d),
            new TrainingAdherenceSample(start.AddSeconds(15), 111d),
        };

        var result = TrainingAdherence.Calculate(samples, null, 110, start, start.AddSeconds(15), completed: true);

        await Assert.That(result is TrainingAdherenceScore { InTargetDuration: var inTarget, Percentage: 50d, Stars: 1 }
            && inTarget == TimeSpan.FromSeconds(5)).IsTrue();
    }

    [Test]
    public async Task Calculate_ReportsNoMeasuredCadenceWhenNoSampleIsEligible()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(1), null),
            new TrainingAdherenceSample(start.AddSeconds(8), null),
        };

        var result = TrainingAdherence.Calculate(samples, 80, 100, start, start.AddSeconds(10), completed: true);

        await Assert.That(result is TrainingAdherenceNoMeasuredCadence).IsTrue();
    }

    [Test]
    public async Task Calculate_ReportsMissingCadenceTarget()
    {
        var result = TrainingAdherence.Calculate(
            [],
            null,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            completed: true);

        await Assert.That(result is TrainingAdherenceNoCadenceTarget).IsTrue();
    }

    [Test]
    public async Task Calculate_DoesNotCountTimeAcrossTelemetryGaps()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(5), 90d),
            new TrainingAdherenceSample(start.AddSeconds(20), 90d),
        };

        var result = TrainingAdherence.Calculate(samples, 80, 100, start, start.AddSeconds(30), completed: true);

        await Assert.That(result is TrainingAdherenceScore
        { EligibleDuration: var eligible, InTargetDuration: var inTarget, Percentage: 100d }
            && eligible == TimeSpan.FromSeconds(10)
            && inTarget == TimeSpan.FromSeconds(10)).IsTrue();
    }
}