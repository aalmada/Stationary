using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingTargetRampTests
{
    private static readonly TrainingPowerCapabilities Capabilities = new(0, 1000, 1);

    [Test]
    public async Task Create_RampsFirstSegmentFromHalfTarget()
    {
        var ramp = TrainingTargetRamp.Create(null, Segment(100, TimeSpan.FromMinutes(5)), Capabilities);

        await Assert.That(ramp).IsEqualTo(new TrainingTargetRamp(50, 100, TimeSpan.FromSeconds(15)));
    }

    [Test]
    public async Task GetTargetWatts_StepsEveryFiveSecondsAndReachesTarget()
    {
        var ramp = TrainingTargetRamp.Create(100, Segment(190, TimeSpan.FromMinutes(5)), Capabilities);

        using (Assert.Multiple())
        {
            await Assert.That(ramp.GetTargetWatts(TimeSpan.Zero, Capabilities)).IsEqualTo(130);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.FromSeconds(4.9), Capabilities)).IsEqualTo(130);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.FromSeconds(5), Capabilities)).IsEqualTo(160);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.FromSeconds(10), Capabilities)).IsEqualTo(190);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.FromSeconds(30), Capabilities)).IsEqualTo(190);
        }
    }

    [Test]
    public async Task Create_AppliesDecreasesImmediately()
    {
        var capabilities = new TrainingPowerCapabilities(50, 500, 10);
        var ramp = TrainingTargetRamp.Create(300, Segment(150, TimeSpan.FromMinutes(5)), capabilities);

        using (Assert.Multiple())
        {
            await Assert.That(ramp.Duration).IsEqualTo(TimeSpan.Zero);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.Zero, capabilities)).IsEqualTo(150);
        }
    }

    [Test]
    public async Task GetTargetWatts_RespectsPowerIncrement()
    {
        var capabilities = new TrainingPowerCapabilities(50, 500, 10);
        var ramp = TrainingTargetRamp.Create(150, Segment(300, TimeSpan.FromMinutes(5)), capabilities);

        await Assert.That(ramp.GetTargetWatts(TimeSpan.Zero, capabilities)).IsEqualTo(200);
    }

    [Test]
    public async Task Create_ShortensRampForShortSegments()
    {
        var ramp = TrainingTargetRamp.Create(100, Segment(300, TimeSpan.FromSeconds(30)), Capabilities);

        await Assert.That(ramp.Duration).IsEqualTo(TimeSpan.FromSeconds(7.5));
    }

    [Test]
    [Arguments(100, 100)]
    [Arguments(100, 101)]
    public async Task Create_SkipsRampForUnchangedTargets(int previous, int target)
    {
        var ramp = TrainingTargetRamp.Create(previous, Segment(target, TimeSpan.FromMinutes(5)), Capabilities);

        using (Assert.Multiple())
        {
            await Assert.That(ramp.Duration).IsEqualTo(TimeSpan.Zero);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.Zero, Capabilities)).IsEqualTo(target);
        }
    }

    [Test]
    public async Task Create_SkipsRampWhenSegmentIsTooShortForOneStep()
    {
        var ramp = TrainingTargetRamp.Create(100, Segment(300, TimeSpan.FromSeconds(18)), Capabilities);

        await Assert.That(ramp.Duration).IsEqualTo(TimeSpan.Zero);
    }

    private static CompiledTrainingSegment Segment(int watts, TimeSpan duration) =>
        new(new TrainingSegment("Work", duration, 100d, null, null, "Ride", true), watts);
}