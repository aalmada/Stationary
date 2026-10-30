using System.Buffers.Binary;
using System.Globalization;

namespace Stationary.Ftms.Dashboard.Core;

// Training event labels make exported rides self-describing so targets and bike responses can be compared with telemetry.
public static class TrainingEventLabels
{
    public static string Workout(string name, string functionalThresholdPower, int preferredCadenceRpm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return string.Create(CultureInfo.InvariantCulture, $"Workout {name} · FTP {functionalThresholdPower} W · preferred cadence {preferredCadenceRpm} rpm");
    }

    public static string Interval(int segmentIndex, int segmentCount, CompiledTrainingSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var cadence = segment.Definition switch
        {
            { MinimumCadenceRpm: int minimum, MaximumCadenceRpm: int maximum } => $"{minimum}–{maximum} rpm",
            { MinimumCadenceRpm: int minimum } => $"≥{minimum} rpm",
            { MaximumCadenceRpm: int maximum } => $"≤{maximum} rpm",
            _ => "no cadence target",
        };
        return string.Create(CultureInfo.InvariantCulture, $"Interval {segmentIndex + 1}/{segmentCount} {segment.Definition.Name} · {segment.TargetWatts} W · {cadence}");
    }

    public static string Target(int watts, bool accepted, TimeSpan responseTime, string status) =>
        accepted
            ? string.Create(CultureInfo.InvariantCulture, $"Target {watts} W accepted in {responseTime.TotalMilliseconds:F0} ms")
            : string.Create(CultureInfo.InvariantCulture, $"Target {watts} W not applied after {responseTime.TotalMilliseconds:F0} ms: {status}");

    public static string Pedalling(double? cadenceRpm) =>
        string.Create(CultureInfo.InvariantCulture, $"Pedalling detected at {FormatCadence(cadenceRpm)}");

    public static string Stalled(double? cadenceRpm, int holdWatts) =>
        string.Create(CultureInfo.InvariantCulture, $"Cadence stalled at {FormatCadence(cadenceRpm)} · holding {holdWatts} W");

    public static string Recovered(double? cadenceRpm, int targetWatts) =>
        string.Create(CultureInfo.InvariantCulture, $"Cadence recovered at {FormatCadence(cadenceRpm)} · ramping to {targetWatts} W");

    public static string MachineStatus(FtmsMachineStatusOpcode opcode, ReadOnlySpan<byte> parameters) =>
        opcode switch
        {
            FtmsMachineStatusOpcode.TargetPowerChanged when parameters.Length == 2 =>
                string.Create(CultureInfo.InvariantCulture, $"Bike status {opcode} {BinaryPrimitives.ReadInt16LittleEndian(parameters)} W"),
            _ when parameters.IsEmpty => string.Create(CultureInfo.InvariantCulture, $"Bike status {opcode}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"Bike status {opcode} 0x{Convert.ToHexString(parameters)}"),
        };

    public static string ResistanceLevel(double? level) =>
        level is double value
            ? string.Create(CultureInfo.InvariantCulture, $"Bike resistance level {value:F1}")
            : "Bike resistance level not reported";

    public static string ResistanceFloor(double wattsPerRpm, double cadenceRpm) =>
        string.Create(CultureInfo.InvariantCulture, $"Bike lightest resistance about {wattsPerRpm:F2} W/rpm · {wattsPerRpm * cadenceRpm:F0} W at {cadenceRpm:F0} rpm");

    public static string TargetBelowFloor(int targetWatts, int minimumWatts, double cadenceRpm) =>
        string.Create(CultureInfo.InvariantCulture, $"Target {targetWatts} W is below the bike's about {minimumWatts} W minimum at {cadenceRpm:F0} rpm");

    private static string FormatCadence(double? cadenceRpm) =>
        cadenceRpm is double cadence
            ? string.Create(CultureInfo.InvariantCulture, $"{cadence:F0} rpm")
            : "unknown cadence";
}