using System.Globalization;
using System.Text.Json;

namespace Stationary.Ftms.Dashboard.Core;

public sealed record RideHistoryEntry(
    DateTimeOffset StartedAt,
    TimeSpan Elapsed,
    string Name,
    double AveragePowerWatts,
    double? NormalizedPowerWatts,
    double? AverageHeartRate,
    double? EfficiencyFactor,
    double? VariabilityIndex,
    double? DecouplingPercentage,
    double? TrainingStressScore);

public sealed record RideHistoryDocument(RideHistoryEntry[] Rides);

public sealed record RideTrendRow(string Date, string Name, string Duration, string Power, string HeartRate, string EfficiencyFactor, string Drift);

public sealed record RideTrendReport(string Summary, IReadOnlyList<RideTrendRow> Rows, IReadOnlyList<TelemetrySample> EfficiencyPoints)
{
    public bool HasEfficiencyTrend => EfficiencyPoints.Count >= 2;
}

public static class RideHistory
{
    public const int MaximumRides = 365;

    // Short rides have too little steady data for a meaningful efficiency comparison.
    public static readonly TimeSpan MinimumTrendDuration = TimeSpan.FromMinutes(10);

    private const int TrendBaselineRides = 5;

    public static string GetFileName(DateTimeOffset startedAt) =>
        string.Create(CultureInfo.InvariantCulture, $"ride-{startedAt.UtcDateTime:yyyyMMdd-HHmmss}.json");

    public static RideHistoryEntry CreateEntry(RecordedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var summary = session.Summary;
        var analysis = WorkoutAnalysis.Calculate(session.Samples);
        var heartRates = session.Samples
            .Select(static sample => sample.HeartRateBeatsPerMinute)
            .OfType<double>()
            .Where(static bpm => bpm > 0d)
            .ToArray();
        double? averageHeartRate = heartRates.Length > 0 ? heartRates.Average() : null;
        var normalized = summary.CyclingMetrics.NormalizedPower;
        return new(
            session.StartedAt,
            summary.Elapsed,
            GetName(session),
            summary.AveragePowerWatts,
            normalized,
            averageHeartRate,
            normalized is double np && averageHeartRate is > 0d ? np / averageHeartRate.Value : analysis.PowerHeartRateEfficiency,
            WorkoutAnalysis.GetVariabilityIndex(normalized, summary.AveragePowerWatts),
            analysis.AerobicDecouplingPercentage,
            summary.CyclingMetrics.TrainingStressScore);
    }

    public static RideHistoryEntry[] Upsert(IReadOnlyList<RideHistoryEntry> rides, RideHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(rides);
        ArgumentNullException.ThrowIfNull(entry);
        return [.. rides
            .Where(ride => ride.StartedAt != entry.StartedAt)
            .Append(entry)
            .OrderBy(static ride => ride.StartedAt)
            .TakeLast(MaximumRides)];
    }

    public static RideTrendReport CreateTrend(IReadOnlyList<RideHistoryEntry> rides, TimeZoneInfo timeZone, int rowCount = 8)
    {
        ArgumentNullException.ThrowIfNull(rides);
        ArgumentNullException.ThrowIfNull(timeZone);
        var ordered = rides.OrderBy(static ride => ride.StartedAt).ToArray();
        var rows = ordered
            .TakeLast(rowCount)
            .Reverse()
            .Select(ride => new RideTrendRow(
                TimeZoneInfo.ConvertTime(ride.StartedAt, timeZone).ToString("ddd d MMM", CultureInfo.InvariantCulture),
                ride.Name,
                ride.Elapsed.ToString(ride.Elapsed >= TimeSpan.FromHours(1) ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture),
                ride.NormalizedPowerWatts is double normalized ? $"{ride.AveragePowerWatts:F0} W avg · {normalized:F0} W NP" : $"{ride.AveragePowerWatts:F0} W avg",
                ride.AverageHeartRate is double heartRate ? $"{heartRate:F0} bpm" : "--",
                ride.EfficiencyFactor is double efficiency ? $"EF {efficiency:F2}" : "EF --",
                ride.DecouplingPercentage is double drift ? $"drift {drift:+0.0;-0.0;0.0}%" : string.Empty))
            .ToArray();
        var comparable = ordered
            .Where(static ride => ride.EfficiencyFactor is > 0d && ride.Elapsed >= MinimumTrendDuration)
            .ToArray();
        var points = comparable
            .TakeLast(30)
            .Select(static ride => new TelemetrySample(ride.StartedAt, ride.EfficiencyFactor!.Value))
            .ToArray();
        return new(GetTrendSummary(ordered, comparable), rows, points);
    }

    public static async Task<RideHistoryDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return new([]);
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, RecordedSessionJsonContext.Default.RideHistoryDocument, cancellationToken).ConfigureAwait(false)
            ?? new([]);
    }

    public static async Task WriteAsync(string path, RideHistoryDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, RecordedSessionJsonContext.Default.RideHistoryDocument, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string GetTrendSummary(IReadOnlyList<RideHistoryEntry> rides, IReadOnlyList<RideHistoryEntry> comparable)
    {
        if (rides.Count == 0)
        {
            return "Rides are saved automatically when they end. Your history appears here.";
        }

        if (comparable.Count < 2)
        {
            return "Ride at least 10 minutes with a heart-rate sensor on two or more rides to see your efficiency trend.";
        }

        var latest = comparable[^1];
        var baseline = comparable.SkipLast(1).TakeLast(TrendBaselineRides).Average(static ride => ride.EfficiencyFactor!.Value);
        var change = (latest.EfficiencyFactor!.Value - baseline) / baseline * 100d;
        var direction = change switch
        {
            >= 3d => "up",
            <= -3d => "down",
            _ => "about level",
        };
        var count = int.Min(TrendBaselineRides, comparable.Count - 1);
        return $"Efficiency factor {latest.EfficiencyFactor:F2}, {direction} ({change:+0;-0;0}%) versus your previous {count} {(count == 1 ? "ride" : "rides")} ({baseline:F2}). "
            + "Higher means more power for each heartbeat; compare similar workouts, as fatigue, heat and sleep also move it.";
    }

    private static string GetName(RecordedSession session)
    {
        const string prefix = "Workout ";
        foreach (var marker in session.Events)
        {
            if (marker.Type == SessionEventType.Training && marker.Label.StartsWith(prefix, StringComparison.Ordinal))
            {
                var separator = marker.Label.IndexOf(" · ", StringComparison.Ordinal);
                return separator > prefix.Length ? marker.Label[prefix.Length..separator] : marker.Label[prefix.Length..];
            }
        }

        return "Free ride";
    }
}