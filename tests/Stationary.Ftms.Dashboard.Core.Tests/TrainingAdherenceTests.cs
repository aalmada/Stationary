using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingAdherenceTests
{
    [Test]
    public async Task Calculate_ExcludesSettlingSamplesAndAwardsThreeStarsForPrecision()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(1), 100d),
            new TrainingAdherenceSample(start.AddSeconds(5), 300d),
            new TrainingAdherenceSample(start.AddSeconds(10), 292d),
            new TrainingAdherenceSample(start.AddSeconds(15), 307d),
        };

        var result = TrainingAdherence.Calculate(samples, 300, 5, start, start.AddSeconds(15), completed: true);

        await Assert.That(result is TrainingAdherenceScore { EligibleSamples: 3, InTargetSamples: 3, Percentage: 100d, Stars: 3 }).IsTrue();
    }

    [Test]
    public async Task Calculate_AllowsZeroWattCompiledTargets()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(5), 0d),
            new TrainingAdherenceSample(start.AddSeconds(10), 0d),
        };

        var result = TrainingAdherence.Calculate(samples, 0, 100, start, start.AddSeconds(10), completed: true);

        await Assert.That(result is TrainingAdherenceScore { InTargetSamples: 2, Percentage: 100d, Stars: 3 }).IsTrue();
    }

    [Test]
    public async Task Calculate_ReportsNoMeasuredPowerWhenNoSampleIsEligible()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(1), 250d),
            new TrainingAdherenceSample(start.AddSeconds(8), null),
        };

        var result = TrainingAdherence.Calculate(samples, 250, 5, start, start.AddSeconds(10), completed: true);

        await Assert.That(result is TrainingAdherenceNoMeasuredPower).IsTrue();
    }
}
