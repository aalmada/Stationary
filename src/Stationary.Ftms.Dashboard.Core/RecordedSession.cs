using System.Text.Json.Serialization;

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
    SessionSummary Summary,
    RideContext? Context = null)
{
    public const int CurrentFormatVersion = 2;

    [JsonIgnore]
    public DateTimeOffset StartedAt
    {
        get
        {
            foreach (var marker in Events)
            {
                if (marker.Type == SessionEventType.Start)
                {
                    return marker.Timestamp;
                }
            }

            return Samples.Length > 0 ? Samples[0].CapturedAt : ExportedAt;
        }
    }
}