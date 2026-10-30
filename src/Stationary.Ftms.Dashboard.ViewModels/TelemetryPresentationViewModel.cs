using Microsoft.Maui.Graphics;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using Stationary.Ftms.Dashboard.Core;
using Stationary.Ftms.Dashboard.Services;

namespace Stationary.Ftms.Dashboard.ViewModels;

internal sealed record TelemetryChartWindow(
    IReadOnlyList<TelemetrySample> Power,
    IReadOnlyList<TelemetrySample> Speed,
    IReadOnlyList<TelemetrySample> Cadence);

public sealed partial class TelemetryPresentationViewModel : ReactiveObject
{
    private const string MetricUnitsPreferenceKey = "MetricUnits";
    private const string LargeTelemetryTextPreferenceKey = "LargeTelemetryText";
    private const string HighContrastTelemetryPreferenceKey = "HighContrastTelemetry";
    private const string CompactChartsPreferenceKey = "CompactCharts";
    private const string FunctionalThresholdPowerPreferenceKey = "FunctionalThresholdPower";
    private const string PreferredCadencePreferenceKey = "PreferredCadenceRpm";
    private const string LegacyCadenceTargetOffsetPreferenceKey = "CadenceTargetOffsetRpm";
    private static readonly TimeSpan MaximumTelemetryGap = TimeSpan.FromSeconds(5);
    private readonly IDashboardPlatform platform;
    private bool useMetricUnits;
    private bool useLargeTelemetryText;
    private bool useHighContrastTelemetry;
    private bool useCompactCharts;
    private TelemetrySnapshot? latestSnapshot;
    private readonly WorkoutSessionClock sessionClock = new();
    private readonly CyclingMetricsAccumulator cyclingMetrics = new(MaximumTelemetryGap);
    private uint? previousDistanceCounter;
    private TelemetrySample? previousDistanceSpeedSample;
    private ushort? previousEnergyCounter;
    private double sessionDistanceMeters;
    private int sessionEnergyKilocalories;
    private double sessionPowerTotal;
    private int sessionPowerSampleCount;
    private double? sessionMaximumPower;

    public TelemetryPresentationViewModel(IDashboardPlatform platform)
    {
        this.platform = platform;
        useMetricUnits = platform.GetPreference(MetricUnitsPreferenceKey, true);
        useLargeTelemetryText = platform.GetPreference(LargeTelemetryTextPreferenceKey, false);
        useHighContrastTelemetry = platform.GetPreference(HighContrastTelemetryPreferenceKey, false);
        useCompactCharts = platform.GetPreference(CompactChartsPreferenceKey, false);
        FunctionalThresholdPowerText = platform.GetPreference(FunctionalThresholdPowerPreferenceKey, string.Empty);
        var savedPreferredCadence = platform.GetPreference(PreferredCadencePreferenceKey, string.Empty);
        if (savedPreferredCadence.Length == 0
            && int.TryParse(platform.GetPreference(LegacyCadenceTargetOffsetPreferenceKey, "0"), out var legacyOffset)
            && legacyOffset != 0)
        {
            savedPreferredCadence = (TrainingWorkoutCompiler.ReferenceCadenceRpm + legacyOffset).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (savedPreferredCadence.Length == 0)
        {
            PreferredCadenceText = TrainingWorkoutCompiler.ReferenceCadenceRpm.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (int.TryParse(savedPreferredCadence, out var preferredCadence)
            && TrainingWorkoutCompiler.IsValidPreferredCadence(preferredCadence))
        {
            PreferredCadenceRpm = preferredCadence;
            PreferredCadenceText = savedPreferredCadence;
            PreferredCadenceStatusText = DescribePreferredCadence(preferredCadence);
        }
        else
        {
            PreferredCadenceText = savedPreferredCadence;
            PreferredCadenceStatusText = $"Saved preferred cadence is invalid; {PreferredCadenceRangeText}";
        }
    }

    private static readonly string PreferredCadenceRangeText = string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"enter a whole number from {TrainingWorkoutCompiler.MinimumPreferredCadenceRpm} to {TrainingWorkoutCompiler.MaximumPreferredCadenceRpm} rpm.");

    [Reactive]
    public partial TelemetryInsight TorqueResponseInsight { get; private set; }

    [Reactive]
    public partial TelemetryInsight PowerPacingInsight { get; private set; }

    [Reactive]
    public partial TelemetryInsight SpeedTrendInsight { get; private set; }

    [Reactive]
    public partial TelemetryInsight CadenceConsistencyInsight { get; private set; }

    [ReactiveCommand]
    private void SetMetricUnits(bool value) => UseMetricUnits = value;

    [ReactiveCommand]
    private void SetLargeTelemetryText(bool value) => UseLargeTelemetryText = value;

    [ReactiveCommand]
    private void SetHighContrastTelemetry(bool value) => UseHighContrastTelemetry = value;

    [ReactiveCommand]
    private void SetCompactCharts(bool value) => UseCompactCharts = value;

    [Reactive]
    public partial string FunctionalThresholdPowerText { get; set; }

    [Reactive]
    public partial string PreferredCadenceText { get; set; } = string.Empty;

    [Reactive]
    public partial int PreferredCadenceRpm { get; private set; } = TrainingWorkoutCompiler.ReferenceCadenceRpm;

    [Reactive]
    public partial string PreferredCadenceStatusText { get; private set; } = "Using the authored cadence ranges.";

    [ReactiveCommand]
    private void ApplyPreferredCadence()
    {
        if (!int.TryParse(PreferredCadenceText, out var preferredCadence)
            || !TrainingWorkoutCompiler.IsValidPreferredCadence(preferredCadence))
        {
            PreferredCadenceStatusText = $"Please {PreferredCadenceRangeText}";
            return;
        }

        PreferredCadenceRpm = preferredCadence;
        platform.SetPreference(PreferredCadencePreferenceKey, preferredCadence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        PreferredCadenceStatusText = DescribePreferredCadence(preferredCadence);
    }

    private static string DescribePreferredCadence(int preferredCadence) =>
        preferredCadence == TrainingWorkoutCompiler.ReferenceCadenceRpm
            ? "Using the authored cadence ranges."
            : $"Scaling workout cadence ranges to {preferredCadence} rpm.";

    [Reactive]
    public partial string SessionWorkText { get; private set; } = "--";

    [Reactive]
    public partial string NormalizedPowerText { get; private set; } = "--";

    [Reactive]
    public partial string IntensityFactorText { get; private set; } = "--";

    [Reactive]
    public partial string TrainingStressScoreText { get; private set; } = "--";

    [Reactive]
    public partial string PeakPowerText { get; private set; } = "--";

    [Reactive]
    public partial string SpeedText { get; private set; } = "-- km/h";

    [Reactive]
    public partial double? CurrentSpeedKilometersPerHour { get; private set; }

    [Reactive]
    public partial string PowerText { get; private set; } = "-- W";

    [Reactive]
    public partial string CurrentPowerZoneText { get; private set; } = "Set FTP for zones";

    [Reactive]
    public partial string CadenceText { get; private set; } = "-- rpm";

    [Reactive]
    public partial double? CurrentCadenceRpm { get; private set; }

    [Reactive]
    public partial double? CurrentPowerWatts { get; private set; }

    [Reactive]
    public partial string HealthText { get; private set; } = "Waiting for telemetry";

    [Reactive]
    public partial bool IsStale { get; private set; }

    public bool UseMetricUnits
    {
        get => useMetricUnits;
        set
        {
            if (useMetricUnits == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref useMetricUnits, value);
            platform.SetPreference(MetricUnitsPreferenceKey, value);
            RefreshPresentation();
        }
    }

    public bool UseLargeTelemetryText
    {
        get => useLargeTelemetryText;
        set
        {
            if (useLargeTelemetryText == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref useLargeTelemetryText, value);
            platform.SetPreference(LargeTelemetryTextPreferenceKey, value);
            this.RaisePropertyChanged(nameof(TelemetryValueFontSize));
            this.RaisePropertyChanged(nameof(RideMetricCardHeight));
            this.RaisePropertyChanged(nameof(SessionMetricValueFontSize));
            this.RaisePropertyChanged(nameof(HeartRateValueFontSize));
        }
    }

    public bool UseHighContrastTelemetry
    {
        get => useHighContrastTelemetry;
        set
        {
            if (useHighContrastTelemetry == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref useHighContrastTelemetry, value);
            platform.SetPreference(HighContrastTelemetryPreferenceKey, value);
            this.RaisePropertyChanged(nameof(PowerChartColor));
            this.RaisePropertyChanged(nameof(SpeedChartColor));
            this.RaisePropertyChanged(nameof(CadenceChartColor));
        }
    }

    public bool UseCompactCharts
    {
        get => useCompactCharts;
        set
        {
            if (useCompactCharts == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref useCompactCharts, value);
            platform.SetPreference(CompactChartsPreferenceKey, value);
            this.RaisePropertyChanged(nameof(ShowChartSummaries));
        }
    }

    public bool ShowChartSummaries => !UseCompactCharts;

    public double TelemetryValueFontSize => UseLargeTelemetryText ? 46 : 32;

    public double RideMetricCardHeight => UseLargeTelemetryText ? 132 : 112;

    public double SessionMetricValueFontSize => UseLargeTelemetryText ? 20 : 14;

    public double HeartRateValueFontSize => UseLargeTelemetryText ? 30 : 24;

    public Color PowerChartColor => Color.FromArgb(UseHighContrastTelemetry ? "005A4F" : "147D70");

    public Color SpeedChartColor => Color.FromArgb(UseHighContrastTelemetry ? "005A9C" : "3679A8");

    public Color CadenceChartColor => Color.FromArgb(UseHighContrastTelemetry ? "8A4B00" : "C48630");

    public IReadOnlyList<double> PowerZoneTransitionValues => TryGetFunctionalThresholdPower(out var ftp)
        ? [ftp * 0.56d, ftp * 0.76d, ftp * 0.91d, ftp * 1.06d, ftp * 1.21d, ftp * 1.51d]
        : [];

    [Reactive]
    public partial string PowerChartSummary { get; private set; } = "No samples";

    [Reactive]
    public partial string SpeedChartSummary { get; private set; } = "No samples";

    [Reactive]
    public partial string CadenceChartSummary { get; private set; } = "No samples";

    [Reactive]
    public partial string PowerCadenceSummary { get; private set; } = "Waiting for paired samples";

    [Reactive]
    public partial string SessionDistanceText { get; private set; } = "--";

    [Reactive]
    public partial string SessionElapsedTimeText { get; private set; } = "--:--:--";

    [Reactive]
    public partial string SessionEnergyText { get; private set; } = "--";

    [Reactive]
    public partial string SessionAveragePowerText { get; private set; } = "--";

    [Reactive]
    public partial string SessionMaximumPowerText { get; private set; } = "--";

    [Reactive]
    public partial IReadOnlyList<TelemetrySample> PowerChartSamples { get; private set; } = [];

    [Reactive]
    public partial IReadOnlyList<TelemetrySample> SpeedChartSamples { get; private set; } = [];

    [Reactive]
    public partial IReadOnlyList<TelemetrySample> CadenceChartSamples { get; private set; } = [];

    [Reactive]
    public partial IReadOnlyList<PowerCadenceSample> PowerCadenceSamples { get; private set; } = [];

    public void Present(TelemetrySnapshot snapshot)
    {
        latestSnapshot = snapshot;
        if (!sessionClock.HasStarted)
        {
            StartSession(snapshot.CapturedAt);
            return;
        }

        RefreshPresentation();
    }

    public void StartSession(DateTimeOffset timestamp)
    {
        sessionClock.Start(timestamp);
        ResetSessionStatistics();
        RefreshPresentation();
    }

    public void PauseSession(DateTimeOffset timestamp)
    {
        sessionClock.Pause(timestamp);
        previousDistanceSpeedSample = null;
        RefreshPresentation();
    }

    public void ResumeSession(DateTimeOffset timestamp)
    {
        sessionClock.Resume(timestamp);
        previousDistanceSpeedSample = null;
        RefreshPresentation();
    }

    public void ResetSession()
    {
        var wasPaused = sessionClock.IsPaused;
        if (latestSnapshot is { } snapshot)
        {
            StartSession(snapshot.CapturedAt);
            if (wasPaused)
            {
                PauseSession(snapshot.CapturedAt);
            }

            return;
        }

        sessionClock.Reset();
        ResetSessionStatistics();
        ClearSessionSummary();
    }

    public string CreateSessionCsv() => string.Join(Environment.NewLine,
        "Metric,Value",
        $"Distance,{SessionDistanceText}",
        $"Elapsed time,{SessionElapsedTimeText}",
        $"Energy,{SessionEnergyText}",
        $"Work,{SessionWorkText}",
        $"Average power,{SessionAveragePowerText}",
        $"Maximum power,{SessionMaximumPowerText}",
        $"Normalized power,{NormalizedPowerText}",
        $"Intensity factor,{IntensityFactorText}",
        $"Training stress score,{TrainingStressScoreText}",
        $"Peak power,{PeakPowerText}");

    public SessionSummary GetSessionSummary()
    {
        var metrics = cyclingMetrics.GetMetrics(TryGetFunctionalThresholdPower(out var ftp) ? ftp : null);
        return new(
            sessionDistanceMeters,
            latestSnapshot is { } snapshot ? sessionClock.GetElapsed(snapshot.CapturedAt) ?? TimeSpan.Zero : TimeSpan.Zero,
            sessionPowerSampleCount > 0 ? sessionPowerTotal / sessionPowerSampleCount : 0d,
            sessionMaximumPower ?? 0d,
            metrics);
    }

    private void RefreshPresentation()
    {
        if (latestSnapshot is not { } snapshot)
        {
            return;
        }

        SpeedText = PresentField(snapshot, TelemetryField.Speed, snapshot.SpeedKilometersPerHour, speed => $"{speed:F1} km/h", SpeedText, "-- km/h");
        if ((snapshot.ReportedFields & TelemetryField.Speed) != 0)
        {
            CurrentSpeedKilometersPerHour = snapshot.SpeedKilometersPerHour;
        }

        if (!UseMetricUnits && snapshot.SpeedKilometersPerHour is double speed)
        {
            SpeedText = $"{speed * 0.621_371d:F1} mph";
        }
        PowerText = PresentField(snapshot, TelemetryField.Power, snapshot.PowerWatts, power => $"{power:F0} W", PowerText, "-- W");
        CurrentPowerZoneText = GetCurrentPowerZoneText(snapshot.PowerWatts);
        CadenceText = PresentField(snapshot, TelemetryField.Cadence, snapshot.CadenceRpm, cadence => $"{cadence:F0} rpm", CadenceText, "-- rpm");
        if ((snapshot.ReportedFields & TelemetryField.Cadence) != 0)
        {
            CurrentCadenceRpm = snapshot.CadenceRpm;
        }

        if ((snapshot.ReportedFields & TelemetryField.Power) != 0)
        {
            CurrentPowerWatts = snapshot.PowerWatts;
        }

        HealthText = $"Live - last packet {snapshot.CapturedAt.ToLocalTime():T}";
        IsStale = false;
        TrackSessionStatistics(snapshot);
        UpdateSessionSummary(snapshot);
    }

    internal void SetChartSamples(TelemetryChartWindow? chart)
    {
        PowerChartSamples = chart?.Power ?? [];
        SpeedChartSamples = chart?.Speed ?? [];
        CadenceChartSamples = chart?.Cadence ?? [];
        PowerCadenceSamples = PairPowerAndCadence(PowerChartSamples, CadenceChartSamples);
        PowerChartSummary = GetChartSummary(PowerChartSamples, "W");
        SpeedChartSummary = GetChartSummary(SpeedChartSamples, "km/h");
        CadenceChartSummary = GetChartSummary(CadenceChartSamples, "rpm");
        PowerCadenceSummary = GetPowerCadenceSummary(PowerCadenceSamples);
        UpdateInsights();
        if (latestSnapshot is { } snapshot)
        {
            UpdateSessionSummary(snapshot);
        }
    }

    public void MarkStale()
    {
        IsStale = true;
        HealthText = "Telemetry delayed - last values may be stale";
    }

    public void Clear()
    {
        SpeedText = "-- km/h";
        CurrentSpeedKilometersPerHour = null;
        PowerText = "-- W";
        CurrentPowerZoneText = "Set FTP for zones";
        CadenceText = "-- rpm";
        CurrentCadenceRpm = null;
        CurrentPowerWatts = null;
        HealthText = "Waiting for telemetry";
        IsStale = false;
        SetChartSamples(null);
        latestSnapshot = null;
        sessionClock.Reset();
        ResetSessionStatistics();
        ClearSessionSummary();
    }

    private static string PresentField<T>(TelemetrySnapshot snapshot, TelemetryField field, T? value, Func<T, string> format, string previous, string unavailable)
        where T : struct => (snapshot.ReportedFields & field) == 0
            ? previous
            : value is T measurement
                ? format(measurement)
                : unavailable;

    private void UpdateSessionSummary(TelemetrySnapshot snapshot)
    {
        if (!sessionClock.HasStarted)
        {
            ClearSessionSummary();
            return;
        }

        SessionDistanceText = UpdateSessionDistance(snapshot) is double distance
            ? UseMetricUnits ? $"{distance:F0} m" : $"{distance * 0.000_621_371d:F2} mi"
            : "--";
        SessionElapsedTimeText = sessionClock.GetElapsed(snapshot.CapturedAt)?.ToString(@"hh\:mm\:ss") ?? "--:--:--";
        SessionEnergyText = UpdateSessionEnergy(snapshot.TotalEnergyKilocalories) is int energy ? $"{energy} kcal" : "--";
        SessionAveragePowerText = sessionPowerSampleCount > 0 ? $"{sessionPowerTotal / sessionPowerSampleCount:F0} W" : "--";
        SessionMaximumPowerText = sessionMaximumPower is double maximumPower ? $"{maximumPower:F0} W" : "--";
        UpdateCyclingMetrics();
    }

    private void TrackSessionStatistics(TelemetrySnapshot snapshot)
    {
        if (!sessionClock.HasStarted || sessionClock.IsPaused || snapshot.PowerWatts is not short power)
        {
            return;
        }

        sessionPowerTotal += power;
        sessionPowerSampleCount++;
        sessionMaximumPower = sessionMaximumPower is double currentMaximum
            ? double.Max(currentMaximum, power)
            : power;
        cyclingMetrics.Add(new(snapshot.CapturedAt, power));
    }

    private double? UpdateSessionDistance(TelemetrySnapshot snapshot)
    {
        if (snapshot.MachineType == FtmsMachineDataType.IndoorBike
            && snapshot.PowerWatts is short
            && snapshot.SpeedKilometersPerHour is double speed)
        {
            previousDistanceCounter = null;
            return UpdateDistanceFromSpeed(snapshot.CapturedAt, speed);
        }

        previousDistanceSpeedSample = null;
        return UpdateDistanceFromCounter(snapshot.TotalDistanceMeters);
    }

    private double UpdateDistanceFromSpeed(DateTimeOffset timestamp, double speedKilometersPerHour)
    {
        if (sessionClock.IsPaused || !double.IsFinite(speedKilometersPerHour) || speedKilometersPerHour < 0d)
        {
            previousDistanceSpeedSample = null;
            return sessionDistanceMeters;
        }

        if (previousDistanceSpeedSample is { } previous && timestamp > previous.CapturedAt)
        {
            var interval = timestamp - previous.CapturedAt;
            if (interval <= MaximumTelemetryGap)
            {
                sessionDistanceMeters += ((previous.Value + speedKilometersPerHour) / 2d) * (interval.TotalSeconds / 3.6d);
            }

            previousDistanceSpeedSample = new(timestamp, speedKilometersPerHour);
        }
        else previousDistanceSpeedSample ??= new(timestamp, speedKilometersPerHour);

        return sessionDistanceMeters;
    }

    private double? UpdateDistanceFromCounter(uint? totalDistance)
    {
        if (totalDistance is not uint value)
        {
            return null;
        }

        if (previousDistanceCounter is uint previous && value >= previous)
        {
            sessionDistanceMeters += value - previous;
        }

        previousDistanceCounter = value;
        return sessionDistanceMeters;
    }

    private int? UpdateSessionEnergy(ushort? totalEnergy)
    {
        if (totalEnergy is not ushort value)
        {
            return null;
        }

        if (previousEnergyCounter is ushort previous && value >= previous)
        {
            sessionEnergyKilocalories += value - previous;
        }

        previousEnergyCounter = value;
        return sessionEnergyKilocalories;
    }

    private void ResetSessionStatistics()
    {
        previousDistanceCounter = null;
        previousDistanceSpeedSample = null;
        previousEnergyCounter = null;
        sessionDistanceMeters = 0d;
        sessionEnergyKilocalories = 0;
        sessionPowerTotal = 0d;
        sessionPowerSampleCount = 0;
        sessionMaximumPower = null;
        cyclingMetrics.Reset();
    }

    private void ClearSessionSummary()
    {
        SessionDistanceText = "--";
        SessionElapsedTimeText = "--:--:--";
        SessionEnergyText = "--";
        SessionAveragePowerText = "--";
        SessionMaximumPowerText = "--";
        SessionWorkText = "--";
        NormalizedPowerText = "--";
        IntensityFactorText = "--";
        TrainingStressScoreText = "--";
        PeakPowerText = "--";
    }

    [ReactiveCommand]
    private void ApplyFunctionalThresholdPower()
    {
        if (ushort.TryParse(FunctionalThresholdPowerText, out var watts) && watts > 0)
        {
            platform.SetPreference(FunctionalThresholdPowerPreferenceKey, FunctionalThresholdPowerText);
        }
        else
        {
            platform.RemovePreference(FunctionalThresholdPowerPreferenceKey);
        }

        this.RaisePropertyChanged(nameof(PowerZoneTransitionValues));
        CurrentPowerZoneText = GetCurrentPowerZoneText(latestSnapshot?.PowerWatts);
        UpdateCyclingMetrics();
    }

    private void UpdateCyclingMetrics()
    {
        var ftp = TryGetFunctionalThresholdPower(out var watts)
            ? watts
            : (ushort?)null;
        var metrics = cyclingMetrics.GetMetrics(ftp);
        SessionWorkText = $"{metrics.TotalWorkKilojoules:F1} kJ";
        NormalizedPowerText = metrics.NormalizedPower is double normalizedPower ? $"{normalizedPower:F0} W" : "Gathering 30 s";
        IntensityFactorText = metrics.IntensityFactor is double intensityFactor ? $"{intensityFactor:F2}" : "Set FTP";
        TrainingStressScoreText = metrics.TrainingStressScore is double score ? $"{score:F1}" : "Set FTP";
        PeakPowerText = string.Join(" · ",
            FormatPeak("5s", metrics.PeakPower5Seconds),
            FormatPeak("30s", metrics.PeakPower30Seconds),
            FormatPeak("1m", metrics.PeakPower1Minute),
            FormatPeak("5m", metrics.PeakPower5Minutes));
    }

    private static string FormatPeak(string duration, double? watts) => watts is double value
        ? $"{duration} {value:F0} W"
        : $"{duration} --";

    private bool TryGetFunctionalThresholdPower(out ushort watts) =>
        ushort.TryParse(FunctionalThresholdPowerText, out watts) && watts > 0;

    private string GetCurrentPowerZoneText(double? powerWatts)
    {
        if (!TryGetFunctionalThresholdPower(out var ftp))
        {
            return "Set FTP for zones";
        }

        if (powerWatts is not double power)
        {
            return "Waiting for power";
        }

        var zone = PowerZoneProfile.Create(ftp).GetZone(power);
        return $"{zone.Code} {zone.Name}";
    }

    private static string GetChartSummary(IReadOnlyList<TelemetrySample> samples, string unit)
    {
        if (samples.Count == 0)
        {
            return "No samples";
        }

        var current = samples[^1].Value;
        var average = samples.Average(sample => sample.Value);
        var maximum = samples.Max(sample => sample.Value);
        return $"Now {current:F0} {unit}\n5m avg {average:F0} {unit}\nMax {maximum:F0} {unit}";
    }

    private static double? GetChartAverage(IReadOnlyList<TelemetrySample> samples) => samples.Count > 0
        ? samples.Average(sample => sample.Value)
        : null;

    private void UpdateInsights()
    {
        TorqueResponseInsight = CreatePowerCadenceInsight(PowerCadenceSamples);
        PowerPacingInsight = CreatePacingInsight("Power pacing", PowerChartSamples, "W", 1d);
        SpeedTrendInsight = CreatePacingInsight("Speed trend", SpeedChartSamples, UseMetricUnits ? "km/h" : "mph", UseMetricUnits ? 1d : 0.621_371d);
        CadenceConsistencyInsight = CreateCadenceInsight(CadenceChartSamples);
    }

    private static IReadOnlyList<PowerCadenceSample> PairPowerAndCadence(IReadOnlyList<TelemetrySample> powerSamples, IReadOnlyList<TelemetrySample> cadenceSamples)
    {
        List<PowerCadenceSample> pairedSamples = [with(capacity: int.Min(powerSamples.Count, cadenceSamples.Count))];
        var powerIndex = 0;
        var cadenceIndex = 0;
        while (powerIndex < powerSamples.Count && cadenceIndex < cadenceSamples.Count)
        {
            var power = powerSamples[powerIndex];
            var cadence = cadenceSamples[cadenceIndex];
            if (power.CapturedAt == cadence.CapturedAt)
            {
                pairedSamples.Add(new(power.CapturedAt, power.Value, cadence.Value));
                powerIndex++;
                cadenceIndex++;
            }
            else if (power.CapturedAt < cadence.CapturedAt)
            {
                powerIndex++;
            }
            else
            {
                cadenceIndex++;
            }
        }

        return pairedSamples;
    }

    private static string GetPowerCadenceSummary(IReadOnlyList<PowerCadenceSample> samples)
    {
        if (samples.Count == 0)
        {
            return "Waiting for paired samples";
        }

        var latest = samples[^1];
        var torque = GetTorque(latest);
        var peakTorque = samples.Max(GetTorque);
        return latest.CadenceRpm > 0d
            ? $"Now {latest.PowerWatts:F0} W @ {latest.CadenceRpm:F0} rpm\nTorque {torque:F1} N.m\nPeak {peakTorque:F1} N.m"
            : $"Now {latest.PowerWatts:F0} W @ {latest.CadenceRpm:F0} rpm\nStopped pedalling\nPeak {peakTorque:F1} N.m";
    }

    private static TelemetryInsight CreatePowerCadenceInsight(IReadOnlyList<PowerCadenceSample> samples)
    {
        if (!TryGetMinuteAverages(samples, out var previousPower, out var recentPower, out var previousCadence, out var recentCadence)
            || previousPower <= 0d
            || previousCadence <= 0d
            || recentCadence <= 0d)
        {
            return new("Torque response", "Gathering data", "Two minutes of paired power and cadence reveals how effort changed.");
        }

        var powerChange = (recentPower - previousPower) / previousPower;
        var cadenceChange = (recentCadence - previousCadence) / previousCadence;
        var torqueChange = ((recentPower / recentCadence) - (previousPower / previousCadence)) / (previousPower / previousCadence);
        var response = cadenceChange is >= -0.05d and <= 0.05d
            ? torqueChange switch
            {
                > 0.05d => "Higher torque",
                < -0.05d => "Lower torque",
                _ => "Similar load",
            }
            : "Cadence changed";
        return new("Torque response", response, $"Power {powerChange:+0%;-0%;0%}; cadence {cadenceChange:+0%;-0%;0%}; torque {torqueChange:+0%;-0%;0%} across the last two minutes.");
    }

    private static double GetTorque(PowerCadenceSample sample) => sample.CadenceRpm > 0d
        ? sample.PowerWatts * 30d / (double.Pi * sample.CadenceRpm)
        : 0d;

    private static bool TryGetMinuteAverages(
        IReadOnlyList<PowerCadenceSample> samples,
        out double previousPower,
        out double recentPower,
        out double previousCadence,
        out double recentCadence)
    {
        previousPower = 0d;
        recentPower = 0d;
        previousCadence = 0d;
        recentCadence = 0d;
        if (samples.Count < 2)
        {
            return false;
        }

        var end = samples[^1].CapturedAt;
        var recentCutoff = end - TimeSpan.FromMinutes(1);
        var previousCutoff = recentCutoff - TimeSpan.FromMinutes(1);
        var previousCount = 0;
        var recentCount = 0;
        foreach (var sample in samples)
        {
            if (sample.CapturedAt >= recentCutoff)
            {
                recentPower += sample.PowerWatts;
                recentCadence += sample.CadenceRpm;
                recentCount++;
            }
            else if (sample.CapturedAt >= previousCutoff)
            {
                previousPower += sample.PowerWatts;
                previousCadence += sample.CadenceRpm;
                previousCount++;
            }
        }

        if (previousCount == 0 || recentCount == 0)
        {
            return false;
        }

        previousPower /= previousCount;
        previousCadence /= previousCount;
        recentPower /= recentCount;
        recentCadence /= recentCount;
        return true;
    }

    private static TelemetryInsight CreatePacingInsight(string name, IReadOnlyList<TelemetrySample> samples, string unit, double multiplier)
    {
        if (!TryGetMinuteAverages(samples, out var previous, out var recent))
        {
            return new(name, "Gathering data", "Two minutes of telemetry reveals the recent trend.");
        }

        if (previous == 0)
        {
            return new(name, "Starting", $"Recent one-minute average: {recent * multiplier:F0} {unit}.");
        }

        var change = (recent - previous) / previous;
        var direction = change switch
        {
            > 0.05d => "Building",
            < -0.05d => "Easing",
            _ => "Steady",
        };
        return new(name, direction, $"One-minute average {change:+0%;-0%;0%}: {recent * multiplier:F0} {unit} vs {previous * multiplier:F0} {unit}.");
    }

    private static TelemetryInsight CreateCadenceInsight(IReadOnlyList<TelemetrySample> samples)
    {
        if (!TryGetMinuteAverages(samples, out _, out var average))
        {
            return new("Cadence consistency", "Gathering data", "Two minutes of telemetry reveals cadence stability.");
        }

        var cutoff = samples[^1].CapturedAt - TimeSpan.FromMinutes(1);
        var recent = samples.Where(sample => sample.CapturedAt >= cutoff).Select(sample => sample.Value).ToArray();
        var standardDeviation = double.Sqrt(recent.Average(value => double.Pow(value - average, 2)));
        var variation = average > 0 ? standardDeviation / average : 0;
        var consistency = variation switch
        {
            <= 0.05d => "Very steady",
            <= 0.10d => "Steady",
            _ => "Variable",
        };
        return new("Cadence consistency", consistency, $"One-minute average {average:F0} rpm with {variation:P0} variation.");
    }

    private static bool TryGetMinuteAverages(IReadOnlyList<TelemetrySample> samples, out double previous, out double recent)
    {
        previous = 0;
        recent = 0;
        if (samples.Count < 2)
        {
            return false;
        }

        var end = samples[^1].CapturedAt;
        var recentCutoff = end - TimeSpan.FromMinutes(1);
        var previousCutoff = recentCutoff - TimeSpan.FromMinutes(1);
        var recentSamples = samples.Where(sample => sample.CapturedAt >= recentCutoff).Select(sample => sample.Value).ToArray();
        var previousSamples = samples.Where(sample => sample.CapturedAt >= previousCutoff && sample.CapturedAt < recentCutoff).Select(sample => sample.Value).ToArray();
        if (recentSamples.Length == 0 || previousSamples.Length == 0)
        {
            return false;
        }

        recent = recentSamples.Average();
        previous = previousSamples.Average();
        return true;
    }

}