using System.Collections.ObjectModel;

using ReactiveUI;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.ViewModels;

public sealed class HeartRatePresentationViewModel : ReactiveObject
{
    private const string LactateThresholdPreferenceKey = "HeartRateCyclingLactateThreshold";
    private static readonly TimeSpan ChartWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumTelemetryGap = TimeSpan.FromSeconds(5);
    private TelemetryHistory chartHistory = new(ChartWindow);
    private HeartRateZoneProfile? zoneProfile;
    private HeartRateSessionAnalytics? sessionAnalytics;
    private HeartRateObservation? latestObservation;
    private string lactateThresholdText = Preferences.Default.Get(LactateThresholdPreferenceKey, string.Empty);
    private string currentHeartRateText = "-- bpm";
    private string currentZoneText = "Set cycling LTHR";
    private string currentZoneRangeText = "Zones need your bike threshold.";
    private string healthText = "No external heart-rate sensor connected";
    private string chartSummary = "No samples";
    private string sessionAverageText = "-- bpm";
    private string sessionMaximumText = "-- bpm";
    private bool isStale;
    private bool isSessionPaused;
    private IReadOnlyList<TelemetrySample> chartSamples = [];
    private IReadOnlyList<double> zoneTransitionValues = [];

    public HeartRatePresentationViewModel()
    {
        ZoneDurations = [];
        if (TryCreateCyclingLactateThresholdProfile(LactateThresholdText, out var profile))
        {
            ConfigureProfile(profile, false);
        }
    }

    public ObservableCollection<HeartRateZoneViewModel> ZoneDurations { get; }

    public string LactateThresholdText
    {
        get => lactateThresholdText;
        set => this.RaiseAndSetIfChanged(ref lactateThresholdText, value);
    }

    public string CurrentHeartRateText
    {
        get => currentHeartRateText;
        private set => this.RaiseAndSetIfChanged(ref currentHeartRateText, value);
    }

    public string CurrentZoneText
    {
        get => currentZoneText;
        private set => this.RaiseAndSetIfChanged(ref currentZoneText, value);
    }

    public string CurrentZoneRangeText
    {
        get => currentZoneRangeText;
        private set => this.RaiseAndSetIfChanged(ref currentZoneRangeText, value);
    }

    public string HealthText
    {
        get => healthText;
        private set => this.RaiseAndSetIfChanged(ref healthText, value);
    }

    public string ChartSummary
    {
        get => chartSummary;
        private set => this.RaiseAndSetIfChanged(ref chartSummary, value);
    }

    public string SessionAverageText
    {
        get => sessionAverageText;
        private set => this.RaiseAndSetIfChanged(ref sessionAverageText, value);
    }

    public string SessionMaximumText
    {
        get => sessionMaximumText;
        private set => this.RaiseAndSetIfChanged(ref sessionMaximumText, value);
    }

    public bool IsStale
    {
        get => isStale;
        private set => this.RaiseAndSetIfChanged(ref isStale, value);
    }

    public bool IsZoneProfileConfigured => zoneProfile is not null;

    public IReadOnlyList<TelemetrySample> ChartSamples
    {
        get => chartSamples;
        private set => this.RaiseAndSetIfChanged(ref chartSamples, value);
    }

    public IReadOnlyList<double> ZoneTransitionValues
    {
        get => zoneTransitionValues;
        private set => this.RaiseAndSetIfChanged(ref zoneTransitionValues, value);
    }

    public Color ChartColor => Color.FromArgb("B42318");

    public bool TryApplyCyclingLactateThreshold(out string error)
    {
        if (!TryCreateCyclingLactateThresholdProfile(LactateThresholdText, out var profile))
        {
            error = "Enter a cycling lactate threshold from 50 to 600 bpm.";
            return false;
        }

        ConfigureProfile(profile, true);
        error = string.Empty;
        return true;
    }

    public void Present(HeartRateObservation observation)
    {
        latestObservation = observation;
        chartHistory.Add(new(observation.CapturedAt, observation.BeatsPerMinute));
        ChartSamples = [.. chartHistory.Samples];
        if (!isSessionPaused)
        {
            sessionAnalytics?.Add(observation);
        }

        CurrentHeartRateText = $"{observation.BeatsPerMinute} bpm";
        HealthText = $"Live - last update {observation.CapturedAt.ToLocalTime():T}";
        IsStale = false;
        RefreshPresentation();
    }

    public void MarkStale()
    {
        IsStale = true;
        HealthText = "Heart-rate delayed - last value may be stale";
    }

    public void Clear()
    {
        latestObservation = null;
        chartHistory = new(ChartWindow);
        sessionAnalytics?.Reset();
        ChartSamples = [];
        CurrentHeartRateText = "-- bpm";
        HealthText = "No external heart-rate sensor connected";
        IsStale = false;
        isSessionPaused = false;
        RefreshPresentation();
    }

    public void StartSession()
    {
        chartHistory = new(ChartWindow);
        sessionAnalytics?.Reset();
        ChartSamples = [];
        isSessionPaused = false;
        RefreshPresentation();
    }

    public void PauseSession(DateTimeOffset timestamp)
    {
        if (isSessionPaused)
        {
            return;
        }

        isSessionPaused = true;
        sessionAnalytics?.Pause(timestamp);
        RefreshPresentation();
    }

    public void ResumeSession()
    {
        if (!isSessionPaused)
        {
            return;
        }

        isSessionPaused = false;
        sessionAnalytics?.Resume();
        RefreshPresentation();
    }

    public void ResetSession()
    {
        chartHistory = new(ChartWindow);
        sessionAnalytics?.Reset();
        ChartSamples = [];
        if (latestObservation is { } observation && !isSessionPaused)
        {
            Present(observation);
            return;
        }

        RefreshPresentation();
    }

    public string CreateSessionCsvRows()
    {
        var rows = new List<string>
        {
            $"Heart-rate average,{SessionAverageText}",
            $"Heart-rate maximum,{SessionMaximumText}",
            $"Heart-rate zone profile,{zoneProfile?.Name ?? "Not configured"}",
        };
        foreach (var zone in ZoneDurations)
        {
            rows.Add($"Heart-rate {zone.Code} {zone.Name},{zone.TimeInZoneText}");
        }

        return string.Join(Environment.NewLine, rows);
    }

    private void ConfigureProfile(HeartRateZoneProfile profile, bool persist)
    {
        zoneProfile = profile;
        sessionAnalytics = new(profile, MaximumTelemetryGap);
        ZoneDurations.Clear();
        foreach (var zone in profile.Zones)
        {
            ZoneDurations.Add(new(zone));
        }

        ZoneTransitionValues = [.. profile.Zones.Skip(1).Select(static zone => (double)zone.MinimumBeatsPerMinute)];

        if (persist)
        {
            Preferences.Default.Set(LactateThresholdPreferenceKey, LactateThresholdText);
        }

        this.RaisePropertyChanged(nameof(IsZoneProfileConfigured));
        if (latestObservation is { } observation && !isSessionPaused)
        {
            sessionAnalytics.Add(observation);
        }

        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        var analytics = sessionAnalytics;
        if (analytics is null)
        {
            CurrentZoneText = "Set cycling LTHR";
            CurrentZoneRangeText = "Zones need your bike threshold.";
            SessionAverageText = "-- bpm";
            SessionMaximumText = "-- bpm";
            ChartSummary = GetChartSummary(ChartSamples);
            return;
        }

        var currentZone = latestObservation is { } observation
            ? analytics.Profile.GetZone(observation.BeatsPerMinute)
            : null;
        if (currentZone is { } zone)
        {
            CurrentZoneText = $"{zone.Code} {zone.Name}";
            CurrentZoneRangeText = GetRangeText(zone);
        }
        else
        {
            CurrentZoneText = "Waiting for heart rate";
            CurrentZoneRangeText = "Zones are ready when telemetry arrives.";
        }

        SessionAverageText = analytics.AverageBeatsPerMinute is double average ? $"{average:F0} bpm" : "-- bpm";
        SessionMaximumText = analytics.MaximumBeatsPerMinute is ushort maximum ? $"{maximum} bpm" : "-- bpm";
        ChartSummary = GetChartSummary(ChartSamples);
        var durations = analytics.GetTimeInZones();
        for (var index = 0; index < durations.Count; index++)
        {
            ZoneDurations[index].Update(durations[index], currentZone?.Code == durations[index].Zone.Code);
        }
    }

    private static bool TryCreateCyclingLactateThresholdProfile(string text, out HeartRateZoneProfile profile)
    {
        profile = null!;
        if (!ushort.TryParse(text, out var lactateThreshold) || lactateThreshold is < 50 or > 600)
        {
            return false;
        }

        profile = HeartRateZoneProfile.CreateCyclingLactateThreshold(lactateThreshold);
        return true;
    }

    private static string GetChartSummary(IReadOnlyList<TelemetrySample> samples)
    {
        if (samples.Count == 0)
        {
            return "No samples";
        }

        var current = samples[^1].Value;
        var average = samples.Average(sample => sample.Value);
        var maximum = samples.Max(sample => sample.Value);
        return $"Now {current:F0} bpm\n5m avg {average:F0} bpm\nMax {maximum:F0} bpm";
    }

    private static string GetRangeText(HeartRateZone zone) => zone.MaximumBeatsPerMinute is ushort maximum
        ? $"{zone.MinimumBeatsPerMinute}-{maximum} bpm"
        : $"{zone.MinimumBeatsPerMinute}+ bpm";
}