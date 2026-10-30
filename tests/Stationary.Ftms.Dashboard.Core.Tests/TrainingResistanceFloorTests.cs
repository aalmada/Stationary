using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingResistanceFloorTests
{
    [Test]
    public async Task Observe_LearnsFloorWhenPowerStaysAboveSettledTarget()
    {
        var floor = new TrainingResistanceFloor();
        var start = DateTimeOffset.UnixEpoch;
        var changed = false;
        for (var second = 0; second <= 12; second += 2)
        {
            changed |= floor.Observe(start.AddSeconds(second), 112d, 80d, 90, settled: true);
        }

        using (Assert.Multiple())
        {
            await Assert.That(changed).IsTrue();
            await Assert.That(floor.WattsPerRpm).IsEqualTo(1.4d);
            await Assert.That(floor.EstimateMinimumWatts(80d)).IsEqualTo(112);
            await Assert.That(floor.IsAboveTarget(90, 80d)).IsTrue();
            await Assert.That(floor.IsAboveTarget(170, 80d)).IsFalse();
        }
    }

    [Test]
    public async Task Observe_IgnoresShortSurgesUnsettledTargetsAndLowCadence()
    {
        var floor = new TrainingResistanceFloor();
        var start = DateTimeOffset.UnixEpoch;

        floor.Observe(start, 141d, 87d, 120, settled: true);
        floor.Observe(start.AddSeconds(4), 141d, 87d, 120, settled: true);
        floor.Observe(start.AddSeconds(6), 120d, 80d, 120, settled: true);
        floor.Observe(start.AddSeconds(20), 141d, 87d, 120, settled: true);
        for (var second = 30; second <= 50; second += 2)
        {
            floor.Observe(start.AddSeconds(second), 178d, 81d, 90, settled: false);
            floor.Observe(start.AddSeconds(second + 1), 84d, 64d, 40, settled: true);
        }

        await Assert.That(floor.WattsPerRpm).IsNull();
    }

    [Test]
    public async Task Observe_LowersFloorWithLaterLighterSettledSamples()
    {
        var floor = new TrainingResistanceFloor();
        var start = DateTimeOffset.UnixEpoch;
        for (var second = 0; second <= 10; second += 2)
        {
            floor.Observe(start.AddSeconds(second), 120d, 80d, 90, settled: true);
        }

        var lowered = floor.Observe(start.AddSeconds(20), 112d, 80d, 170, settled: true);
        var lowCadence = floor.Observe(start.AddSeconds(22), 69d, 64d, 80, settled: true);

        using (Assert.Multiple())
        {
            await Assert.That(lowered).IsTrue();
            await Assert.That(lowCadence).IsFalse();
            await Assert.That(floor.WattsPerRpm).IsEqualTo(1.4d);
            await Assert.That(floor.EstimateMinimumWatts(60d)).IsNull();
        }
    }

    [Test]
    public async Task Observe_IgnoresSubPercentJitter()
    {
        var floor = Learned(102d, 73d);

        var changed = floor.Observe(DateTimeOffset.UnixEpoch.AddSeconds(30), 100d, 72d, 90, settled: true);

        await Assert.That(changed).IsFalse();
    }

    [Test]
    [Arguments(100, 75, 75)]
    [Arguments(100, null, 72)]
    [Arguments(100, 60, 72)]
    [Arguments(130, 75, 93)]
    public async Task GetClosestCadence_StaysWithinBandAndFloorRange(int target, int? minimumCadence, int expected)
    {
        var floor = Learned(112d, 80d);

        await Assert.That(floor.GetClosestCadence(target, minimumCadence)).IsEqualTo(expected);
    }

    [Test]
    public async Task GetClosestCadence_IsNullBeforeLearning()
    {
        await Assert.That(new TrainingResistanceFloor().GetClosestCadence(100, 75)).IsNull();
    }

    private static TrainingResistanceFloor Learned(double power, double cadence)
    {
        var floor = new TrainingResistanceFloor();
        for (var second = 0; second <= 10; second += 2)
        {
            floor.Observe(DateTimeOffset.UnixEpoch.AddSeconds(second), power, cadence, 80, settled: true);
        }

        return floor;
    }
}