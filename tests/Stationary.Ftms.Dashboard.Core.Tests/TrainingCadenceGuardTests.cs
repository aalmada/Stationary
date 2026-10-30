using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingCadenceGuardTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Test]
    public async Task Update_StallsAfterStallDurationBelowThreshold()
    {
        var guard = new TrainingCadenceGuard();

        using (Assert.Multiple())
        {
            await Assert.That(guard.Update(Start, 30d)).IsEqualTo(TrainingCadenceGuardTransition.None);
            await Assert.That(guard.Update(Start.AddSeconds(1), 30d)).IsEqualTo(TrainingCadenceGuardTransition.None);
            await Assert.That(guard.Update(Start.Add(TrainingCadenceGuard.StallDuration), 8d)).IsEqualTo(TrainingCadenceGuardTransition.Stalled);
            await Assert.That(guard.IsStalled).IsTrue();
        }
    }

    [Test]
    public async Task Update_BriefDipDoesNotStall()
    {
        var guard = new TrainingCadenceGuard();

        guard.Update(Start, 30d);
        guard.Update(Start.AddSeconds(2), 40d);

        using (Assert.Multiple())
        {
            await Assert.That(guard.Update(Start.AddSeconds(4), 30d)).IsEqualTo(TrainingCadenceGuardTransition.None);
            await Assert.That(guard.IsStalled).IsFalse();
        }
    }

    [Test]
    public async Task Update_RecoversOnFirstPedallingSample()
    {
        var guard = new TrainingCadenceGuard();
        guard.Update(Start, 20d);
        guard.Update(Start.AddSeconds(3), 20d);

        using (Assert.Multiple())
        {
            await Assert.That(guard.Update(Start.AddSeconds(4), 26d)).IsEqualTo(TrainingCadenceGuardTransition.None);
            await Assert.That(guard.Update(Start.AddSeconds(5), 40d)).IsEqualTo(TrainingCadenceGuardTransition.None);
            await Assert.That(guard.IsStalled).IsTrue();
            await Assert.That(guard.Update(Start.AddSeconds(6), 46d)).IsEqualTo(TrainingCadenceGuardTransition.Recovered);
            await Assert.That(guard.IsStalled).IsFalse();
            await Assert.That(guard.Update(Start.AddSeconds(7), 70d)).IsEqualTo(TrainingCadenceGuardTransition.None);
        }
    }

    [Test]
    public async Task Update_IgnoresMissingCadence()
    {
        var guard = new TrainingCadenceGuard();
        guard.Update(Start, 20d);

        await Assert.That(guard.Update(Start.AddSeconds(5), null)).IsEqualTo(TrainingCadenceGuardTransition.None);
    }

    [Test]
    [Arguments(null, false)]
    [Arguments(44.9d, false)]
    [Arguments(45d, true)]
    public async Task IsPedalling_RequiresPedallingCadence(double? cadence, bool expected)
    {
        await Assert.That(TrainingCadenceGuard.IsPedalling(cadence)).IsEqualTo(expected);
    }
}