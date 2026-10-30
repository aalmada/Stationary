using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingEventLabelsTests
{
    [Test]
    public async Task Interval_DescribesPositionPowerAndCadenceRange()
    {
        var segment = new CompiledTrainingSegment(new TrainingSegment("Endurance 1", TimeSpan.FromMinutes(6), 62d, 75, 85, "Settle.", true), 124);

        await Assert.That(TrainingEventLabels.Interval(2, 8, segment)).IsEqualTo("Interval 3/8 Endurance 1 · 124 W · 75–85 rpm");
    }

    [Test]
    public async Task Interval_DescribesSegmentsWithoutCadenceTarget()
    {
        var segment = new CompiledTrainingSegment(new TrainingSegment("Cool down", TimeSpan.FromMinutes(5), 45d, null, null, "Ease off.", false), 90);

        await Assert.That(TrainingEventLabels.Interval(7, 8, segment)).IsEqualTo("Interval 8/8 Cool down · 90 W · no cadence target");
    }

    [Test]
    public async Task Target_ReportsResponseTimeAndRejectionReason()
    {
        await Assert.That(TrainingEventLabels.Target(124, true, TimeSpan.FromMilliseconds(182.4), "ignored"))
            .IsEqualTo("Target 124 W accepted in 182 ms");
        await Assert.That(TrainingEventLabels.Target(124, false, TimeSpan.FromSeconds(10), "SetTargetPower timed out."))
            .IsEqualTo("Target 124 W not applied after 10000 ms: SetTargetPower timed out.");
    }

    [Test]
    public async Task CadenceGuardLabels_IncludeMeasuredCadence()
    {
        await Assert.That(TrainingEventLabels.Stalled(31.6d, 0)).IsEqualTo("Cadence stalled at 32 rpm · holding 0 W");
        await Assert.That(TrainingEventLabels.Recovered(46d, 124)).IsEqualTo("Cadence recovered at 46 rpm · ramping to 124 W");
        await Assert.That(TrainingEventLabels.Pedalling(null)).IsEqualTo("Pedalling detected at unknown cadence");
    }

    [Test]
    public async Task MachineStatus_DescribesBikeStatus()
    {
        using (Assert.Multiple())
        {
            await Assert.That(TrainingEventLabels.MachineStatus(FtmsMachineStatusOpcode.TargetPowerChanged, [0x82, 0x00])).IsEqualTo("Bike status TargetPowerChanged 130 W");
            await Assert.That(TrainingEventLabels.MachineStatus(FtmsMachineStatusOpcode.StoppedOrPausedByUser, [0x02])).IsEqualTo("Bike status StoppedOrPausedByUser 0x02");
            await Assert.That(TrainingEventLabels.MachineStatus(FtmsMachineStatusOpcode.StartedOrResumedByUser, [])).IsEqualTo("Bike status StartedOrResumedByUser");
            await Assert.That(TrainingEventLabels.ResistanceLevel(4.5d)).IsEqualTo("Bike resistance level 4.5");
            await Assert.That(TrainingEventLabels.ResistanceLevel(null)).IsEqualTo("Bike resistance level not reported");
        }
    }
}