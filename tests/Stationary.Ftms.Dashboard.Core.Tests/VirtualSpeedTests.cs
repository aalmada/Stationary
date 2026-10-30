using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class VirtualSpeedTests
{
    [Test]
    [Arguments(0d, 0d)]
    [Arguments(-5d, 0d)]
    [Arguments(100d, 26.0d)]
    [Arguments(200d, 34.0d)]
    [Arguments(300d, 39.4d)]
    public async Task FromPower_SolvesFlatRoadBalance(double powerWatts, double expectedKilometresPerHour)
    {
        await Assert.That(double.Round(VirtualSpeed.FromPower(powerWatts), 1)).IsEqualTo(expectedKilometresPerHour);
    }

    [Test]
    public async Task FromPower_BalancesDragAtSolvedSpeed()
    {
        var metresPerSecond = VirtualSpeed.FromPower(150d) / 3.6d;
        var resistance = (VirtualSpeed.RollingResistanceCoefficient * VirtualSpeed.SystemMassKilograms * 9.80665d)
            + (0.5d * VirtualSpeed.AirDensityKilogramsPerCubicMetre * VirtualSpeed.DragAreaSquareMetres * metresPerSecond * metresPerSecond);

        await Assert.That(resistance * metresPerSecond).IsEqualTo(150d * VirtualSpeed.DrivetrainEfficiency).Within(1e-9);
    }

    [Test]
    public async Task Apply_ReplacesCadenceBasedIndoorBikeSpeed()
    {
        var snapshot = new TelemetrySnapshot(DateTimeOffset.UnixEpoch, FtmsMachineDataType.IndoorBike, 26.5d, 74d, 103, null, null, null);
        var faster = VirtualSpeed.Apply(snapshot with { PowerWatts = 137 });
        var applied = VirtualSpeed.Apply(snapshot);

        using (Assert.Multiple())
        {
            await Assert.That(applied.SpeedKilometersPerHour).IsEqualTo(VirtualSpeed.FromPower(103d));
            await Assert.That(applied.ReportedFields.HasFlag(TelemetryField.Speed)).IsTrue();
            await Assert.That(faster.SpeedKilometersPerHour!.Value).IsGreaterThan(applied.SpeedKilometersPerHour!.Value);
            await Assert.That(VirtualSpeed.Apply(snapshot with { CadenceRpm = 0d }).SpeedKilometersPerHour).IsEqualTo(0d);
        }
    }

    [Test]
    public async Task Apply_KeepsReportedSpeedWithoutPowerOrForOtherMachines()
    {
        var withoutPower = new TelemetrySnapshot(DateTimeOffset.UnixEpoch, FtmsMachineDataType.IndoorBike, 26.5d, 74d, null, null, null, null);
        var treadmill = withoutPower with { MachineType = FtmsMachineDataType.Treadmill, PowerWatts = 103 };

        using (Assert.Multiple())
        {
            await Assert.That(VirtualSpeed.Apply(withoutPower)).IsEqualTo(withoutPower);
            await Assert.That(VirtualSpeed.Apply(treadmill)).IsEqualTo(treadmill);
        }
    }
}