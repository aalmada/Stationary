using System.Globalization;

namespace Stationary.Ftms.Dashboard.Core;

public static class WorkoutAnalysisText
{
    public static string Efficiency(WorkoutAnalysisMetrics metrics) =>
        metrics.PowerHeartRateEfficiency is double efficiency ? $"{efficiency:F2} W/bpm (NP ÷ avg HR)" : "Gathering paired data";

    public static string Decoupling(WorkoutAnalysisMetrics metrics) => metrics.DecouplingAvailability switch
    {
        DecouplingAvailability.Available when metrics.AerobicDecouplingPercentage is double decoupling =>
            $"{decoupling:+0.0;-0.0;0.0}%{(decoupling > 5d ? " · above 5%: aerobic base still building" : " · steady")}",
        DecouplingAvailability.Variable => metrics.VariabilityIndex is double index
            ? $"Not meaningful: power varied (VI {index:F2})"
            : "Not meaningful: power varied",
        DecouplingAvailability.NoData => "Needs paired power and heart rate",
        _ => "Requires 10 minutes",
    };

    public static string Variability(double? variabilityIndex) => variabilityIndex switch
    {
        double index when index <= WorkoutAnalysis.MaximumSteadyVariabilityIndex => $"{index:F2} · steady",
        double index => $"{index:F2} · variable (intervals, ramps or surges)",
        _ => "--",
    };

    public static string Cadence(WorkoutAnalysisMetrics metrics) =>
        metrics.AverageCadenceRpm is double average
            ? $"{average:F0} rpm · {metrics.CadenceVariationPercentage:F1}% variation · preferred {metrics.PreferredCadenceMinimumRpm:F0}-{metrics.PreferredCadenceMaximumRpm:F0}"
            : "Gathering cadence";

    public static string TorqueExposure(WorkoutAnalysisMetrics metrics) =>
        metrics.LowCadenceHighTorqueExposure.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    public static string Recovery(WorkoutAnalysisMetrics metrics) =>
        metrics.HeartRateRecoveryBeatsPerMinute is double recovery ? $"−{recovery:F0} bpm in 60 s" : "Waiting for recovery";
}

public sealed record SavedRideReport(
    string Title,
    string Distance,
    string Elapsed,
    string Work,
    string AveragePower,
    string MaximumPower,
    string NormalizedPower,
    string IntensityFactor,
    string TrainingStressScore,
    string PeakPower,
    string HeartRate,
    string Efficiency,
    string Decoupling,
    string Cadence,
    string TorqueExposure)
{
    private static readonly TimeSpan MaximumSampleGap = TimeSpan.FromSeconds(5);

    public string VariabilityIndex { get; init; } = "--";

    public string Recovery { get; init; } = "--";

    public IReadOnlyList<RideZoneShare> HeartRateZones { get; init; } = [];

    public string HeartRateZoneSource { get; init; } = string.Empty;

    public IReadOnlyList<RideZoneShare> PowerZones { get; init; } = [];

    public string PowerZoneSource { get; init; } = string.Empty;

    public string Adherence { get; init; } = string.Empty;

    public IReadOnlyList<RideSegmentAdherence> AdherenceSegments { get; init; } = [];

    public RideChartSeries Charts { get; init; } = RideChartSeries.Empty;

    public bool HasHeartRateZones => HeartRateZones.Count > 0;

    public bool HasPowerZones => PowerZones.Count > 0;

    public bool HasAdherence => AdherenceSegments.Count > 0;

    public bool HasHeartRateChart => Charts.HeartRate.Count > 1;

    public bool HasCharts => Charts.Power.Count > 1 || HasHeartRateChart;

    // Zones and FTP stored with the ride are preferred; current settings are the fallback for rides saved before they were recorded.
    public static SavedRideReport Create(
        RecordedSession session,
        TimeZoneInfo timeZone,
        bool useMetricUnits,
        ushort? currentFunctionalThresholdPowerWatts = null,
        HeartRateZoneProfile? currentHeartRateZones = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeZone);

        var summary = session.Summary;
        var metrics = summary.CyclingMetrics;
        var analysis = WorkoutAnalysis.Calculate(session.Samples);
        var heartRates = session.Samples
            .Select(static sample => sample.HeartRateBeatsPerMinute)
            .OfType<double>()
            .Where(static bpm => bpm > 0)
            .ToArray();
        var savedAt = TimeZoneInfo.ConvertTime(session.ExportedAt, timeZone);
        var variabilityIndex = WorkoutAnalysis.GetVariabilityIndex(metrics.NormalizedPower, summary.AveragePowerWatts);
        var averageHeartRate = heartRates.Length > 0 ? heartRates.Average() : 0d;
        var (heartRateZones, heartRateZoneSource) = GetHeartRateZones(session, currentHeartRateZones);
        var (powerZones, powerZoneSource) = GetPowerZones(session, currentFunctionalThresholdPowerWatts);
        var adherence = RideAnalytics.GetAdherence(session, MaximumSampleGap);
        return new SavedRideReport(
            Title: string.Create(CultureInfo.InvariantCulture, $"Saved {savedAt:ddd d MMM, HH:mm}"),
            Distance: useMetricUnits ? $"{summary.DistanceMeters:F0} m" : $"{summary.DistanceMeters * 0.000_621_371d:F2} mi",
            Elapsed: summary.Elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            Work: $"{metrics.TotalWorkKilojoules:F1} kJ",
            AveragePower: $"{summary.AveragePowerWatts:F0} W",
            MaximumPower: $"{summary.MaximumPowerWatts:F0} W",
            NormalizedPower: metrics.NormalizedPower is double normalized ? $"{normalized:F0} W" : "--",
            IntensityFactor: metrics.IntensityFactor is double intensity ? $"{intensity:F2}" : "No FTP set",
            TrainingStressScore: metrics.TrainingStressScore is double score ? $"{score:F1}" : "No FTP set",
            PeakPower: string.Join(" · ",
                FormatPeak("5s", metrics.PeakPower5Seconds),
                FormatPeak("30s", metrics.PeakPower30Seconds),
                FormatPeak("1m", metrics.PeakPower1Minute),
                FormatPeak("5m", metrics.PeakPower5Minutes)),
            HeartRate: heartRates.Length > 0
                ? $"{heartRates.Average():F0} bpm average · {heartRates.Max():F0} bpm peak"
                : "No heart-rate data",
            Efficiency: metrics.NormalizedPower is double normalizedPower && averageHeartRate > 0d
                ? $"{normalizedPower / averageHeartRate:F2} W/bpm (NP ÷ avg HR)"
                : analysis.PowerHeartRateEfficiency is null ? "No paired heart-rate data" : WorkoutAnalysisText.Efficiency(analysis),
            Decoupling: analysis.DecouplingAvailability == DecouplingAvailability.TooShort
                ? "Ride shorter than 10 minutes"
                : WorkoutAnalysisText.Decoupling(analysis),
            Cadence: analysis.AverageCadenceRpm is null ? "No cadence data" : WorkoutAnalysisText.Cadence(analysis),
            TorqueExposure: WorkoutAnalysisText.TorqueExposure(analysis))
        {
            VariabilityIndex = WorkoutAnalysisText.Variability(variabilityIndex),
            Recovery = analysis.HeartRateRecoveryBeatsPerMinute is null
                ? "No recovery after a hard effort recorded"
                : WorkoutAnalysisText.Recovery(analysis),
            HeartRateZones = heartRateZones,
            HeartRateZoneSource = heartRateZoneSource,
            PowerZones = powerZones,
            PowerZoneSource = powerZoneSource,
            Adherence = adherence is null
                ? string.Empty
                : string.Join(" · ", new[]
                {
                    adherence.PowerFraction is double power ? $"Power on target {power * 100d:F0}% of the time" : null,
                    adherence.HeartRateFraction is double heartRate ? $"heart rate in range {heartRate * 100d:F0}%" : null,
                }.OfType<string>()),
            AdherenceSegments = adherence?.Segments ?? [],
            Charts = RideAnalytics.GetChartSeries(session),
        };
    }

    private static (IReadOnlyList<RideZoneShare> Zones, string Source) GetHeartRateZones(RecordedSession session, HeartRateZoneProfile? current)
    {
        var (zones, source) = session.Context is { HeartRateZones: { Length: > 0 } stored } context
            ? (stored, $"{context.HeartRateZoneProfileName ?? "Heart-rate"} zones used during the ride")
            : current is not null
                ? (RideContext.FromProfile(current), $"{current.Name} zones (current settings)")
                : (null, "Set up heart-rate zones to see time in zones.");
        if (zones is null)
        {
            return ([], source);
        }

        var times = RideAnalytics.GetHeartRateZoneTimes(session.Samples, zones, MaximumSampleGap);
        return times.Count > 0 ? (times, source) : ([], "No heart-rate data");
    }

    private static (IReadOnlyList<RideZoneShare> Zones, string Source) GetPowerZones(RecordedSession session, ushort? current)
    {
        var (ftp, source) = session.Context?.FunctionalThresholdPowerWatts is ushort stored and > 0
            ? (stored, $"FTP {stored} W at ride time")
            : current is ushort currentFtp and > 0
                ? (currentFtp, $"FTP {currentFtp} W (current setting)")
                : ((ushort)0, "Set your FTP to see time in power zones.");
        if (ftp == 0)
        {
            return ([], source);
        }

        var times = RideAnalytics.GetPowerZoneTimes(session.Samples, PowerZoneProfile.Create(ftp), MaximumSampleGap);
        return times.Count > 0 ? (times, source) : ([], "No power data");
    }

    private static string FormatPeak(string duration, double? watts) => watts is double value
        ? $"{duration} {value:F0} W"
        : $"{duration} --";
}