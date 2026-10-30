namespace Stationary.Ftms.Dashboard.Core;

// Indoor bikes often report speed from a fixed cadence-to-wheel ratio, which ignores power. This estimates the speed the
// same power would sustain on a flat road with no wind, where drive power balances rolling and aerodynamic drag.
public static class VirtualSpeed
{
    public const double SystemMassKilograms = 83d;
    public const double RollingResistanceCoefficient = 0.004d;
    public const double DragAreaSquareMetres = 0.32d;
    public const double AirDensityKilogramsPerCubicMetre = 1.225d;
    public const double DrivetrainEfficiency = 0.976d;
    private const double Gravity = 9.80665d;

    public static double FromPower(double powerWatts)
    {
        if (!(powerWatts > 0d))
        {
            return 0d;
        }

        // Solves v³ + p·v − q = 0 (Cardano); p > 0 gives exactly one real root.
        var drag = 0.5d * AirDensityKilogramsPerCubicMetre * DragAreaSquareMetres;
        var p = RollingResistanceCoefficient * SystemMassKilograms * Gravity / drag;
        var q = powerWatts * DrivetrainEfficiency / drag;
        var root = double.Sqrt((q * q / 4d) + (p * p * p / 27d));
        var metresPerSecond = double.Cbrt((q / 2d) + root) + double.Cbrt((q / 2d) - root);
        return metresPerSecond * 3.6d;
    }

    public static TelemetrySnapshot Apply(TelemetrySnapshot snapshot) =>
        snapshot.MachineType == FtmsMachineDataType.IndoorBike && snapshot.PowerWatts is short power
            ? snapshot with
            {
                SpeedKilometersPerHour = snapshot.CadenceRpm is 0d ? 0d : FromPower(power),
                ReportedFields = snapshot.ReportedFields | TelemetryField.Speed,
            }
            : snapshot;
}