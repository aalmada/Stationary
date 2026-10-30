using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingAdherenceTests
{
    [Test]
    public async Task Calculate_ExcludesTenSecondSettlingPeriodAndScoresCadenceRange()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(1), 70d),
            new TrainingAdherenceSample(start.AddSeconds(5), 70d),
            new TrainingAdherenceSample(start.AddSeconds(9), 70d),
            new TrainingAdherenceSample(start.AddSeconds(10), 80d),
            new TrainingAdherenceSample(start.AddSeconds(15), 82d),
            new TrainingAdherenceSample(start.AddSeconds(20), 91d),
        };

        var result = TrainingAdherence.Calculate(samples, 80, 90, start, start.AddSeconds(25), completed: true);

        await Assert.That(result is TrainingAdherenceScore
        { EligibleDuration: var eligible, InTargetDuration: var inTarget, Percentage: var percentage, Stars: 1 }
            && eligible == TimeSpan.FromSeconds(15)
            && inTarget == TimeSpan.FromSeconds(10)
            && double.Abs(percentage - (200d / 3d)) < 0.001d).IsTrue();
    }

    [Test]
    public async Task Calculate_SupportsCadenceTargetsWithOneOpenBoundaryAfterSettling()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(10), 100d),
            new TrainingAdherenceSample(start.AddSeconds(15), 111d),
            new TrainingAdherenceSample(start.AddSeconds(20), 111d),
        };

        var result = TrainingAdherence.Calculate(samples, null, 110, start, start.AddSeconds(25), completed: true);

        await Assert.That(result is TrainingAdherenceScore
        { EligibleDuration: var eligible, InTargetDuration: var inTarget, Percentage: var percentage, Stars: 1 }
            && eligible == TimeSpan.FromSeconds(15)
            && inTarget == TimeSpan.FromSeconds(5)
            && double.Abs(percentage - (100d / 3d)) < 0.001d).IsTrue();
    }

    [Test]
    public async Task Calculate_ReportsNoEligibleCadenceWhenIntervalEndsDuringSettling()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(5), 90d),
            new TrainingAdherenceSample(start.AddSeconds(9), 90d),
        };

        var result = TrainingAdherence.Calculate(samples, 80, 100, start, start.AddSeconds(10), completed: true);

        await Assert.That(result is TrainingAdherenceNoEligibleCadence).IsTrue();
    }

    [Test]
    public async Task Calculate_ReportsNoEligibleCadenceWhenSamplesHaveNoCadence()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(1), null),
            new TrainingAdherenceSample(start.AddSeconds(15), null),
        };

        var result = TrainingAdherence.Calculate(samples, 80, 100, start, start.AddSeconds(20), completed: true);

        await Assert.That(result is TrainingAdherenceNoEligibleCadence).IsTrue();
    }

    [Test]
    public async Task GetSettlingDuration_AllowsLongerTransitionWhenCadenceTargetChanges()
    {
        var steady = new TrainingSegment("Steady", TimeSpan.FromMinutes(1), 60d, 85, 95, "Ride", true);
        var climb = steady with { Name = "Climb", MinimumCadenceRpm = 70, MaximumCadenceRpm = 80 };
        var harder = steady with { Name = "Harder", FunctionalThresholdPowerPercentage = 70d };
        var free = steady with { Name = "Float", MinimumCadenceRpm = null, MaximumCadenceRpm = null };

        using (Assert.Multiple())
        {
            await Assert.That(TrainingAdherence.GetSettlingDuration(steady, climb)).IsEqualTo(TimeSpan.FromSeconds(15));
            await Assert.That(TrainingAdherence.GetSettlingDuration(free, steady)).IsEqualTo(TimeSpan.FromSeconds(15));
            await Assert.That(TrainingAdherence.GetSettlingDuration(steady, harder)).IsEqualTo(TimeSpan.FromSeconds(10));
            await Assert.That(TrainingAdherence.GetSettlingDuration(null, steady)).IsEqualTo(TimeSpan.FromSeconds(10));
            await Assert.That(TrainingAdherence.GetSettlingDuration(steady, free)).IsEqualTo(TimeSpan.FromSeconds(10));
        }
    }

    [Test]
    public async Task Calculate_UsesProvidedSettlingDuration()
    {
        var start = DateTimeOffset.UnixEpoch;
        var samples = new[]
        {
            new TrainingAdherenceSample(start.AddSeconds(10), 60d),
            new TrainingAdherenceSample(start.AddSeconds(15), 75d),
            new TrainingAdherenceSample(start.AddSeconds(20), 75d),
        };

        var result = TrainingAdherence.Calculate(samples, 70, 80, start, start.AddSeconds(25), completed: true, TimeSpan.FromSeconds(15));

        await Assert.That(result is TrainingAdherenceScore
        { EligibleDuration: var eligible, Percentage: 100d }
            && eligible == TimeSpan.FromSeconds(10)).IsTrue();
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
            new TrainingAdherenceSample(start.AddSeconds(10), 90d),
            new TrainingAdherenceSample(start.AddSeconds(25), 90d),
        };

        var result = TrainingAdherence.Calculate(samples, 80, 100, start, start.AddSeconds(30), completed: true);

        await Assert.That(result is TrainingAdherenceScore
        { EligibleDuration: var eligible, InTargetDuration: var inTarget, Percentage: 100d }
            && eligible == TimeSpan.FromSeconds(10)
            && inTarget == TimeSpan.FromSeconds(10)).IsTrue();
    }
}