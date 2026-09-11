using System.Reactive;

using ReactiveUI;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.ViewModels;

internal sealed record TelemetryChartWindow(
    IReadOnlyList<TelemetrySample> Power,
    IReadOnlyList<TelemetrySample> Speed,
    IReadOnlyList<TelemetrySample> Cadence);

public sealed class TelemetryPresentationViewModel : ReactiveObject
{
    private const string MetricUnitsPreferenceKey = "MetricUnits";
    private const string LargeTelemetryTextPreferenceKey = "LargeTelemetryText";
    private const string HighContrastTelemetryPreferenceKey = "HighContrastTelemetry";
    private string speedText = "-- km/h";
    private string powerText = "-- W";
    private string cadenceText = "-- rpm";
    private string healthText = "Waiting for telemetry";
    private string powerChartSummary = "No samples";
    private string speedChartSummary = "No samples";
    private string cadenceChartSummary = "No samples";
    private string powerCadenceSummary = "Waiting for paired samples";
    private string sessionDistanceText = "--";
    private string sessionElapsedTimeText = "--:--:--";
    private string sessionEnergyText = "--";
    private string sessionAveragePowerText = "--";
    private string sessionMaximumPowerText = "--";
    private bool isStale;
    private bool useMetricUnits = Preferences.Default.Get(MetricUnitsPreferenceKey, true);
    private bool useLargeTelemetryText = Preferences.Default.Get(LargeTelemetryTextPreferenceKey, false);
    private bool useHighContrastTelemetry = Preferences.Default.Get(HighContrastTelemetryPreferenceKey, false);
    private IReadOnlyList<TelemetrySample> powerChartSamples = [];
    private IReadOnlyList<TelemetrySample> speedChartSamples = [];
    private IReadOnlyList<TelemetrySample> cadenceChartSamples = [];
    private IReadOnlyList<PowerCadenceSample> powerCadenceSamples = [];
    private TelemetrySnapshot? latestSnapshot;
    private readonly WorkoutSessionClock sessionClock = new();
    private uint? previousDistanceCounter;
    private ushort? previousEnergyCounter;
    private double sessionDistanceMeters;
    private int sessionEnergyKilocalories;
    private double sessionPowerTotal;
    private int sessionPowerSampleCount;
    private double? sessionMaximumPower;
    private TelemetryInsight torqueResponseInsight;
    private TelemetryInsight powerPacingInsight;
    private TelemetryInsight speedTrendInsight;
    private TelemetryInsight cadenceConsistencyInsight;

    public TelemetryPresentationViewModel()
    {
        SetMetricUnitsCommand = ReactiveCommand.Create<bool>(value => UseMetricUnits = value);
        SetLargeTelemetryTextCommand = ReactiveCommand.Create<bool>(value => UseLargeTelemetryText = value);
        SetHighContrastTelemetryCommand = ReactiveCommand.Create<bool>(value => UseHighContrastTelemetry = value);
    }

    public TelemetryInsight TorqueResponseInsight
    {
        get => torqueResponseInsight;
        private set => this.RaiseAndSetIfChanged(ref torqueResponseInsight, value);
    }

    public TelemetryInsight PowerPacingInsight
    {
        get => powerPacingInsight;
        private set => this.RaiseAndSetIfChanged(ref powerPacingInsight, value);
    }

    public TelemetryInsight SpeedTrendInsight
    {
        get => speedTrendInsight;
        private set => this.RaiseAndSetIfChanged(ref speedTrendInsight, value);
    }

    public TelemetryInsight CadenceConsistencyInsight
    {
        get => cadenceConsistencyInsight;
        private set => this.RaiseAndSetIfChanged(ref cadenceConsistencyInsight, value);
    }

    public ReactiveCommand<bool, Unit> SetMetricUnitsCommand { get; }

    public ReactiveCommand<bool, Unit> SetLargeTelemetryTextCommand { get; }

    public ReactiveCommand<bool, Unit> SetHighContrastTelemetryCommand { get; }

    public string SpeedText
    {
        get => speedText;
        private set => this.RaiseAndSetIfChanged(ref speedText, value);
    }

    public string PowerText
    {
        get => powerText;
        private set => this.RaiseAndSetIfChanged(ref powerText, value);
    }

    public string CadenceText
    {
        get => cadenceText;
        private set => this.RaiseAndSetIfChanged(ref cadenceText, value);
    }

    public string HealthText
    {
        get => healthText;
        private set => this.RaiseAndSetIfChanged(ref healthText, value);
    }

    public bool IsStale
    {
        get => isStale;
        private set => this.RaiseAndSetIfChanged(ref isStale, value);
    }

    public bool UseMetricUnits
    {
        get => useMetricUnits;
        set
        {
            if (this.RaiseAndSetIfChanged(ref useMetricUnits, value))
            {
                Preferences.Default.Set(MetricUnitsPreferenceKey, value);
                RefreshPresentation();
            }
        }
    }

    public bool UseLargeTelemetryText
    {
        get => useLargeTelemetryText;
        set
        {
            if (this.RaiseAndSetIfChanged(ref useLargeTelemetryText, value))
            {
                Preferences.Default.Set(LargeTelemetryTextPreferenceKey, value);
                this.RaisePropertyChanged(nameof(TelemetryValueFontSize));
                this.RaisePropertyChanged(nameof(RideMetricCardHeight));
                this.RaisePropertyChanged(nameof(SessionMetricValueFontSize));
                this.RaisePropertyChanged(nameof(HeartRateValueFontSize));
            }
        }
    }

    public bool UseHighContrastTelemetry
    {
        get => useHighContrastTelemetry;
        set
        {
            if (this.RaiseAndSetIfChanged(ref useHighContrastTelemetry, value))
            {
                Preferences.Default.Set(HighContrastTelemetryPreferenceKey, value);
                this.RaisePropertyChanged(nameof(PowerChartColor));
                this.RaisePropertyChanged(nameof(SpeedChartColor));
                this.RaisePropertyChanged(nameof(CadenceChartColor));
            }
        }
    }

    public double TelemetryValueFontSize => UseLargeTelemetryText ? 46 : 32;

    public double RideMetricCardHeight => UseLargeTelemetryText ? 132 : 112;

    public double SessionMetricValueFontSize => UseLargeTelemetryText ? 20 : 14;

    public double HeartRateValueFontSize => UseLargeTelemetryText ? 30 : 24;

    public Color PowerChartColor => Color.FromArgb(UseHighContrastTelemetry ? "005A4F" : "147D70");

    public Color SpeedChartColor => Color.FromArgb(UseHighContrastTelemetry ? "005A9C" : "3679A8");

    public Color CadenceChartColor => Color.FromArgb(UseHighContrastTelemetry ? "8A4B00" : "C48630");

    public string PowerChartSummary
    {
        get => powerChartSummary;
        private set => this.RaiseAndSetIfChanged(ref powerChartSummary, value);
    }

    public string SpeedChartSummary
    {
        get => speedChartSummary;
        private set => this.RaiseAndSetIfChanged(ref speedChartSummary, value);
    }

    public string CadenceChartSummary
    {
        get => cadenceChartSummary;
        private set => this.RaiseAndSetIfChanged(ref cadenceChartSummary, value);
    }

    public string PowerCadenceSummary
    {
        get => powerCadenceSummary;
        private set => this.RaiseAndSetIfChanged(ref powerCadenceSummary, value);
    }

    public string SessionDistanceText
    {
        get => sessionDistanceText;
        private set => this.RaiseAndSetIfChanged(ref sessionDistanceText, value);
    }

    public string SessionElapsedTimeText
    {
        get => sessionElapsedTimeText;
        private set => this.RaiseAndSetIfChanged(ref sessionElapsedTimeText, value);
    }

    public string SessionEnergyText
    {
        get => sessionEnergyText;
        private set => this.RaiseAndSetIfChanged(ref sessionEnergyText, value);
    }

    public string SessionAveragePowerText
    {
        get => sessionAveragePowerText;
        private set => this.RaiseAndSetIfChanged(ref sessionAveragePowerText, value);
    }

    public string SessionMaximumPowerText
    {
        get => sessionMaximumPowerText;
        private set => this.RaiseAndSetIfChanged(ref sessionMaximumPowerText, value);
    }

    public IReadOnlyList<TelemetrySample> PowerChartSamples
    {
        get => powerChartSamples;
        private set => this.RaiseAndSetIfChanged(ref powerChartSamples, value);
    }

    public IReadOnlyList<TelemetrySample> SpeedChartSamples
    {
        get => speedChartSamples;
        private set => this.RaiseAndSetIfChanged(ref speedChartSamples, value);
    }

    public IReadOnlyList<TelemetrySample> CadenceChartSamples
    {
        get => cadenceChartSamples;
        private set => this.RaiseAndSetIfChanged(ref cadenceChartSamples, value);
    }

    public IReadOnlyList<PowerCadenceSample> PowerCadenceSamples
    {
        get => powerCadenceSamples;
        private set => this.RaiseAndSetIfChanged(ref powerCadenceSamples, value);
    }

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
        RefreshPresentation();
    }

    public void ResumeSession(DateTimeOffset timestamp)
    {
        sessionClock.Resume(timestamp);
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
        $"Average power,{SessionAveragePowerText}",
        $"Maximum power,{SessionMaximumPowerText}");

    private void RefreshPresentation()
    {
        if (latestSnapshot is not { } snapshot)
        {
            return;
        }

        SpeedText = PresentField(snapshot, TelemetryField.Speed, snapshot.SpeedKilometersPerHour, speed => $"{speed:F1} km/h", SpeedText, "-- km/h");
        if (!UseMetricUnits && snapshot.SpeedKilometersPerHour is double speed)
        {
            SpeedText = $"{speed * 0.621_371d:F1} mph";
        }
        PowerText = PresentField(snapshot, TelemetryField.Power, snapshot.PowerWatts, power => $"{power:F0} W", PowerText, "-- W");
        CadenceText = PresentField(snapshot, TelemetryField.Cadence, snapshot.CadenceRpm, cadence => $"{cadence:F0} rpm", CadenceText, "-- rpm");
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
        PowerText = "-- W";
        CadenceText = "-- rpm";
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

        SessionDistanceText = UpdateSessionDistance(snapshot.TotalDistanceMeters) is double distance
            ? UseMetricUnits ? $"{distance:F0} m" : $"{distance * 0.000_621_371d:F2} mi"
            : "--";
        SessionElapsedTimeText = sessionClock.GetElapsed(snapshot.CapturedAt)?.ToString(@"hh\:mm\:ss") ?? "--:--:--";
        SessionEnergyText = UpdateSessionEnergy(snapshot.TotalEnergyKilocalories) is int energy ? $"{energy} kcal" : "--";
        SessionAveragePowerText = sessionPowerSampleCount > 0 ? $"{sessionPowerTotal / sessionPowerSampleCount:F0} W" : "--";
        SessionMaximumPowerText = sessionMaximumPower is double maximumPower ? $"{maximumPower:F0} W" : "--";
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
    }

    private double? UpdateSessionDistance(uint? totalDistance)
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
        previousEnergyCounter = null;
        sessionDistanceMeters = 0d;
        sessionEnergyKilocalories = 0;
        sessionPowerTotal = 0d;
        sessionPowerSampleCount = 0;
        sessionMaximumPower = null;
    }

    private void ClearSessionSummary()
    {
        SessionDistanceText = "--";
        SessionElapsedTimeText = "--:--:--";
        SessionEnergyText = "--";
        SessionAveragePowerText = "--";
        SessionMaximumPowerText = "--";
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
        var pairedSamples = new List<PowerCadenceSample>();
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