using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using Microsoft.Maui.Graphics;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using Stationary.Ftms.Dashboard.Core;
using Stationary.Ftms.Dashboard.Services;

namespace Stationary.Ftms.Dashboard.ViewModels;

public sealed partial class HeartRatePresentationViewModel : ReactiveObject, IDisposable
{
    private const string LactateThresholdPreferenceKey = "HeartRateCyclingLactateThreshold";
    private static readonly TimeSpan ChartWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumTelemetryGap = TimeSpan.FromSeconds(5);
    private readonly IDashboardPlatform platform;
    private readonly ISubject<HeartRateObservation> chartObservations = Subject.Synchronize(new Subject<HeartRateObservation>());
    private readonly SerialDisposable chartRegionSubscription = new();
    private TelemetryHistory chartHistory = new(ChartWindow);
    private HeartRateZoneProfile? zoneProfile;
    private HeartRateSessionAnalytics? sessionAnalytics;
    private HeartRateObservation? latestObservation;
    private bool isSessionPaused;

    public HeartRatePresentationViewModel(IDashboardPlatform platform)
    {
        this.platform = platform;
        LactateThresholdText = platform.GetPreference(LactateThresholdPreferenceKey, string.Empty);
        ZoneDurations = [];
        if (TryCreateCyclingLactateThresholdProfile(LactateThresholdText, out var profile))
        {
            ConfigureProfile(profile, false);
        }
    }

    public ObservableCollection<HeartRateZoneViewModel> ZoneDurations { get; }

    [Reactive]
    public partial string LactateThresholdText { get; set; }

    [ReactiveCommand]
    private void EditLactateThreshold(string text) => LactateThresholdText = text;

    [Reactive]
    public partial string CurrentHeartRateText { get; private set; } = "-- bpm";

    [Reactive]
    public partial string CurrentZoneText { get; private set; } = "Set cycling LTHR";

    [Reactive]
    public partial string CurrentZoneRangeText { get; private set; } = "Zones need your bike threshold.";

    [Reactive]
    public partial string HealthText { get; private set; } = "No external heart-rate sensor connected";

    [Reactive]
    public partial string ChartSummary { get; private set; } = "No samples";

    [Reactive]
    public partial string SessionAverageText { get; private set; } = "-- bpm";

    [Reactive]
    public partial string SessionMaximumText { get; private set; } = "-- bpm";

    [Reactive]
    public partial bool IsStale { get; private set; }

    public bool IsZoneProfileConfigured => zoneProfile is not null;

    [Reactive]
    public partial IReadOnlyList<TelemetrySample> ChartSamples { get; private set; } = [];

    [Reactive]
    public partial IReadOnlyList<TransitionRegion<Color>> ChartBackgroundRegions { get; private set; } = [];

    [Reactive]
    public partial IReadOnlyList<double> ZoneTransitionValues { get; private set; } = [];

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
        UpdateChartSamples();
        chartObservations.OnNext(observation);
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
        UpdateChartSamples();
        ResetChartRegionPipeline();
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
        UpdateChartSamples();
        ResetChartRegionPipeline();
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
        UpdateChartSamples();
        ResetChartRegionPipeline();
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
        UpdateChartSamples();
        ResetChartRegionPipeline();

        if (persist)
        {
            platform.SetPreference(LactateThresholdPreferenceKey, LactateThresholdText);
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
        var totalDuration = TimeSpan.FromTicks(durations.Sum(static duration => duration.Duration.Ticks));
        for (var index = 0; index < durations.Count; index++)
        {
            ZoneDurations[index].Update(durations[index], totalDuration, currentZone?.Code == durations[index].Zone.Code);
        }
    }

    private void UpdateChartSamples()
    {
        ChartSamples = [.. chartHistory.Samples];
    }

    private void ResetChartRegionPipeline()
    {
        var profile = zoneProfile;
        if (profile is null)
        {
            chartRegionSubscription.Disposable = null;
            ChartBackgroundRegions = [];
            return;
        }

        var retainedObservations = ChartSamples
            .Select(static sample => new HeartRateObservation(sample.CapturedAt, checked((ushort)sample.Value)))
            .ToObservable();
        chartRegionSubscription.Disposable = retainedObservations
            .Concat(chartObservations)
            .ToTransitionRegions(
                static observation => observation.CapturedAt,
                observation => profile.GetZone(observation.BeatsPerMinute).Code,
                ChartWindow)
            .Select(static regions => (IReadOnlyList<TransitionRegion<Color>>)[.. regions.Select(region =>
                new TransitionRegion<Color>(region.Start, region.End, GetZoneColor(region.State)))])
            .Subscribe(
                regions => ChartBackgroundRegions = regions,
                _ => ChartBackgroundRegions = []);
    }

    public void Dispose()
    {
        EditLactateThresholdCommand.Dispose();
        chartRegionSubscription.Dispose();
        chartObservations.OnCompleted();
    }

    private static Color GetZoneColor(string code) => code switch
    {
        "Z1" => Color.FromArgb("CC86A5C4"),
        "Z2" => Color.FromArgb("CC4A90E2"),
        "Z3" => Color.FromArgb("CC3C9D67"),
        "Z4" => Color.FromArgb("CCE0B332"),
        "Z5a" => Color.FromArgb("CCE67E22"),
        "Z5b" => Color.FromArgb("CCD94A4A"),
        "Z5c" => Color.FromArgb("CCA6469B"),
        _ => Colors.Transparent,
    };

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