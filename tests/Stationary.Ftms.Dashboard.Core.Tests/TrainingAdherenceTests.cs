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

        using (Assert.Multiple())
        {
            await Assert.That(result.EligibleSamples).IsEqualTo(3);
            await Assert.That(result.InTargetSamples).IsEqualTo(3);
            await Assert.That(result.Percentage).IsEqualTo(100d);
            await Assert.That(result.Stars).IsEqualTo(3);
        }
    }
}