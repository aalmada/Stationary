namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct RecordedTelemetrySample(
    DateTimeOffset CapturedAt,
    double? PowerWatts,
    double? SpeedKilometersPerHour,
    double? CadenceRpm,
    double? HeartRateBeatsPerMinute);

public readonly record struct SessionSummary(
    double DistanceMeters,
    TimeSpan Elapsed,
    double AveragePowerWatts,
    double MaximumPowerWatts,
    CyclingMetrics CyclingMetrics);

public sealed record RecordedSession(
    int FormatVersion,
    DateTimeOffset ExportedAt,
    RecordedTelemetrySample[] Samples,
    SessionEventMarker[] Events,
    SessionSummary Summary);