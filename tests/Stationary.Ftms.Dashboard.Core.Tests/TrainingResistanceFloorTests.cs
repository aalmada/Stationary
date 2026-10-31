using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingResistanceFloorTests
{
    [Test]
    public async Task Observe_LearnsFloorWhenPowerStaysAboveSettledTarget()
    {
        var start = DateTimeOffset.UnixEpoch;
        var (floor, changed) = Enumerable.Range(0, 7).Aggregate(
            (Floor: default(TrainingResistanceFloor), Changed: false),
            (state, step) => state.Floor.Observe(start.AddSeconds(step * 2), 112d, 80d, 90, settled: true) is var (next, changedNow)
                ? (next, state.Changed || changedNow)
                : state);

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
        var start = DateTimeOffset.UnixEpoch;
        (double Power, double Cadence, int Target, bool Settled, int Second)[] samples =
        [
            (141d, 87d, 120, true, 0),
            (141d, 87d, 120, true, 4),
            (120d, 80d, 120, true, 6),
            (141d, 87d, 120, true, 20),
            .. Enumerable.Range(0, 11).SelectMany(static step => new[]
            {
                (178d, 81d, 90, false, 30 + (step * 2)),
                (84d, 64d, 40, true, 31 + (step * 2)),
            }),
        ];
        var floor = samples.Aggregate(
            default(TrainingResistanceFloor),
            (current, sample) => current.Observe(start.AddSeconds(sample.Second), sample.Power, sample.Cadence, sample.Target, sample.Settled).Floor);

        await Assert.That(floor.WattsPerRpm).IsNull();
    }

    [Test]
    public async Task Observe_LowersFloorWithLaterLighterSettledSamples()
    {
        var start = DateTimeOffset.UnixEpoch;
        var learned = Learned(120d, 80d, targetWatts: 90);
        var (floor, lowered) = learned.Observe(start.AddSeconds(20), 112d, 80d, 170, settled: true);
        var (_, lowCadence) = floor.Observe(start.AddSeconds(22), 69d, 64d, 80, settled: true);

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

        var (_, changed) = floor.Observe(DateTimeOffset.UnixEpoch.AddSeconds(30), 100d, 72d, 90, settled: true);

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
        await Assert.That(default(TrainingResistanceFloor).GetClosestCadence(100, 75)).IsNull();
    }

    private static TrainingResistanceFloor Learned(double power, double cadence, int targetWatts = 80) =>
        Enumerable.Range(0, 6).Aggregate(
            default(TrainingResistanceFloor),
            (floor, step) => floor.Observe(DateTimeOffset.UnixEpoch.AddSeconds(step * 2), power, cadence, targetWatts, settled: true).Floor);
}