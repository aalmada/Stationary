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
    public async Task GetTargetWatts_StepsEveryThreeSecondsAndReachesTarget()
    {
        var ramp = TrainingTargetRamp.Create(100, Segment(200, TimeSpan.FromMinutes(5)), Capabilities);

        using (Assert.Multiple())
        {
            await Assert.That(ramp.GetTargetWatts(TimeSpan.Zero, Capabilities)).IsEqualTo(120);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.FromSeconds(2.9), Capabilities)).IsEqualTo(120);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.FromSeconds(3), Capabilities)).IsEqualTo(140);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.FromSeconds(9), Capabilities)).IsEqualTo(180);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.FromSeconds(12), Capabilities)).IsEqualTo(200);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.FromSeconds(30), Capabilities)).IsEqualTo(200);
        }
    }

    [Test]
    public async Task GetTargetWatts_RampsDownAndRespectsPowerIncrement()
    {
        var capabilities = new TrainingPowerCapabilities(50, 500, 10);
        var ramp = TrainingTargetRamp.Create(300, Segment(150, TimeSpan.FromMinutes(5)), capabilities);

        using (Assert.Multiple())
        {
            await Assert.That(ramp.GetTargetWatts(TimeSpan.Zero, capabilities)).IsEqualTo(270);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.FromSeconds(6), capabilities)).IsEqualTo(210);
            await Assert.That(ramp.GetTargetWatts(TimeSpan.FromSeconds(12), capabilities)).IsEqualTo(150);
        }
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
        var ramp = TrainingTargetRamp.Create(100, Segment(300, TimeSpan.FromSeconds(10)), Capabilities);

        await Assert.That(ramp.Duration).IsEqualTo(TimeSpan.Zero);
    }

    private static CompiledTrainingSegment Segment(int watts, TimeSpan duration) =>
        new(new TrainingSegment("Work", duration, 100d, null, null, "Ride", true), watts);
}