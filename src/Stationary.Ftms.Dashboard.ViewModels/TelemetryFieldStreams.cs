using System.Reactive.Linq;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard;

internal sealed record TelemetryFieldDefinition(
    string Name,
    Func<TelemetrySnapshot, bool> IsReported,
    Func<TelemetrySnapshot, double?> SelectValue,
    Func<TelemetrySnapshot, bool> ShouldAdvance,
    TimeSpan StallThreshold);

internal readonly record struct TelemetryFieldUpdate(string Name, bool IsObserved, bool IsUnavailable, bool IsStalled);

internal static class TelemetryFieldStreams
{
    private static readonly TimeSpan NoStall = TimeSpan.MaxValue;
    private static readonly TimeSpan MechanicalStall = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan EnergyStall = TimeSpan.FromMinutes(1);

    public static IReadOnlyList<TelemetryFieldDefinition> Definitions { get; } =
    [
        Sample("Average speed", TelemetryField.AverageSpeed, snapshot => snapshot.AverageSpeedKilometersPerHour),
        Sample("Cadence", TelemetryField.Cadence, snapshot => snapshot.CadenceRpm),
        Progress("Total distance", TelemetryField.TotalDistance, snapshot => snapshot.TotalDistanceMeters, MechanicalStall),
        Sample("Inclination", TelemetryField.Inclination, snapshot => snapshot.InclinationTenths),
        Sample("Elevation gain", TelemetryField.PositiveElevation | TelemetryField.NegativeElevation, snapshot => Sum(snapshot.PositiveElevationGainMeters, snapshot.NegativeElevationGainMeters)),
        Sample("Pace", TelemetryField.InstantaneousPace | TelemetryField.AveragePace, snapshot => snapshot.InstantaneousPaceSecondsPerKilometre ?? snapshot.AveragePaceSecondsPerKilometre),
        Progress("Step count", TelemetryField.StepCount, snapshot => snapshot.StepCount, MechanicalStall),
        Sample("Resistance level", TelemetryField.Resistance, snapshot => snapshot.ResistanceTenths),
        Progress("Stride count", TelemetryField.StrideCount, snapshot => snapshot.StrideCount, MechanicalStall),
        Progress("Total energy", TelemetryField.TotalEnergy, snapshot => snapshot.TotalEnergyKilocalories, EnergyStall),
        Sample("Energy per hour", TelemetryField.EnergyPerHour, snapshot => snapshot.EnergyPerHourKilocalories),
        Sample("Energy per minute", TelemetryField.EnergyPerMinute, snapshot => snapshot.EnergyPerMinuteKilocalories),
        Sample("Heart rate", TelemetryField.HeartRate, snapshot => snapshot.HeartRateBeatsPerMinute is > 0 ? snapshot.HeartRateBeatsPerMinute : null),
        Sample("Metabolic equivalent", TelemetryField.MetabolicEquivalent, snapshot => snapshot.MetabolicEquivalentTenths),
        Progress("Elapsed time", TelemetryField.ElapsedTime, snapshot => snapshot.ElapsedTimeSeconds, TimeSpan.FromSeconds(5)),
        Progress("Remaining time", TelemetryField.RemainingTime, snapshot => snapshot.RemainingTimeSeconds, TimeSpan.FromSeconds(5)),
        Sample("Power measurement", TelemetryField.Power, snapshot => snapshot.PowerWatts),
        Sample("Force on belt", TelemetryField.ForceOnBelt, snapshot => snapshot.ForceOnBeltNewtons),
    ];

    public static IObservable<TelemetryFieldUpdate> ObserveField(
        this IObservable<TelemetrySnapshot> source,
        TelemetryFieldDefinition definition) => source
        .Select(snapshot => new TelemetryFieldSample(
            snapshot.CapturedAt,
            definition.IsReported(snapshot),
            definition.SelectValue(snapshot),
            definition.ShouldAdvance(snapshot)))
        .Scan(
            TelemetryFieldObservation.Empty,
            (observation, sample) => observation.Observe(sample, definition.StallThreshold))
        .Select(observation => new TelemetryFieldUpdate(
            definition.Name,
            observation.IsObserved,
            observation.IsUnavailable,
            observation.IsStalled))
        .DistinctUntilChanged();

    private static TelemetryFieldDefinition Sample(string name, TelemetryField fields, Func<TelemetrySnapshot, double?> selectValue) =>
        new(name, snapshot => IsAnyReported(snapshot, fields), selectValue, _ => false, NoStall);

    private static TelemetryFieldDefinition Progress(string name, TelemetryField fields, Func<TelemetrySnapshot, double?> selectValue, TimeSpan stallThreshold) =>
        new(name, snapshot => IsAnyReported(snapshot, fields), selectValue, IsActive, stallThreshold);

    private static bool IsAnyReported(TelemetrySnapshot snapshot, TelemetryField fields) => (snapshot.ReportedFields & fields) != 0;

    private static bool IsActive(TelemetrySnapshot snapshot) =>
        snapshot.SpeedKilometersPerHour is > 0 || snapshot.CadenceRpm is > 0 || snapshot.PowerWatts is > 0;

    private static double? Sum(ushort? first, ushort? second) => first.HasValue || second.HasValue ? first.GetValueOrDefault() + second.GetValueOrDefault() : null;
}