using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingCadenceGuardTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Test]
    public async Task Update_StallsAfterStallDurationBelowThreshold()
    {
        var (guard, transitions) = Run(
            (Start, 30d),
            (Start.AddSeconds(1), 30d),
            (Start.Add(TrainingCadenceGuard.StallDuration), 8d));

        using (Assert.Multiple())
        {
            await Assert.That(transitions.SequenceEqual([
                TrainingCadenceGuardTransition.None,
                TrainingCadenceGuardTransition.None,
                TrainingCadenceGuardTransition.Stalled])).IsTrue();
            await Assert.That(guard.IsStalled).IsTrue();
        }
    }

    [Test]
    public async Task Update_BriefDipDoesNotStall()
    {
        var (guard, transitions) = Run((Start, 30d), (Start.AddSeconds(2), 40d), (Start.AddSeconds(4), 30d));

        using (Assert.Multiple())
        {
            await Assert.That(transitions[^1]).IsEqualTo(TrainingCadenceGuardTransition.None);
            await Assert.That(guard.IsStalled).IsFalse();
        }
    }

    [Test]
    public async Task Update_RecoversOnFirstPedallingSample()
    {
        var (stalled, _) = Run((Start, 20d), (Start.AddSeconds(3), 20d), (Start.AddSeconds(4), 26d), (Start.AddSeconds(5), 40d));
        var (recovered, recovery) = stalled.Update(Start.AddSeconds(6), 46d);
        var (_, afterRecovery) = recovered.Update(Start.AddSeconds(7), 70d);

        using (Assert.Multiple())
        {
            await Assert.That(stalled.IsStalled).IsTrue();
            await Assert.That(recovery).IsEqualTo(TrainingCadenceGuardTransition.Recovered);
            await Assert.That(recovered.IsStalled).IsFalse();
            await Assert.That(afterRecovery).IsEqualTo(TrainingCadenceGuardTransition.None);
        }
    }

    [Test]
    public async Task Update_IgnoresMissingCadence()
    {
        var (guard, _) = default(TrainingCadenceGuard).Update(Start, 20d);

        await Assert.That(guard.Update(Start.AddSeconds(5), null).Transition).IsEqualTo(TrainingCadenceGuardTransition.None);
    }

    [Test]
    [Arguments(null, false)]
    [Arguments(44.9d, false)]
    [Arguments(45d, true)]
    public async Task IsPedalling_RequiresPedallingCadence(double? cadence, bool expected)
    {
        await Assert.That(TrainingCadenceGuard.IsPedalling(cadence)).IsEqualTo(expected);
    }

    private static (TrainingCadenceGuard Guard, TrainingCadenceGuardTransition[] Transitions) Run(params (DateTimeOffset At, double? Cadence)[] samples) =>
        samples.Aggregate(
            (Guard: default(TrainingCadenceGuard), Transitions: Array.Empty<TrainingCadenceGuardTransition>()),
            static (state, sample) => state.Guard.Update(sample.At, sample.Cadence) is var (guard, transition)
                ? (guard, [.. state.Transitions, transition])
                : state);
}