using System.Globalization;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using Stationary.Ftms.Dashboard.Core;
using Stationary.Ftms.Dashboard.Services;

namespace Stationary.Ftms.Dashboard.ViewModels;

public readonly record struct RideSceneSample(RideGameInput Input, int HeartRateZoneIndex, IReadOnlyList<HeartRateZone> HeartRateZones, double? SegmentRemainingSeconds);

// One rung of the heart-rate zone ladder on the scene HUD.
public sealed record RideSceneZone(int Index, int Count, string Code, string Name, string RangeText, bool IsCurrent, bool IsTarget);

// Drives the gamified 3D ride scene: tracks session achievements from 1 Hz ride samples, exposes the HUD, and decides
// when the scene window shows.
public sealed partial class RideSceneViewModel : ReactiveObject, IDisposable
{
    internal const string AutoOpenPreferenceKey = "RideSceneAutoOpenOnSecondDisplay";
    internal static readonly TimeSpan AchievementDisplayDuration = TimeSpan.FromSeconds(6);
    private const int RecentAchievementCount = 5;
    private readonly CompositeDisposable subscriptions = [];
    private readonly IDashboardPlatform platform;

    internal RideSceneViewModel(IObservable<bool> rideInProgress, IObservable<RideSceneSample> samples, IDashboardPlatform platform, IScheduler timerScheduler)
    {
        this.platform = platform;
        AutoOpenOnSecondDisplay = platform.GetPreference(AutoOpenPreferenceKey, true);
        var progress = rideInProgress.DistinctUntilChanged().Replay(1).RefCount();
        var game = progress
            .Where(static active => active)
            .Select(_ => samples
                .TakeUntil(progress.Where(static active => !active))
                .Scan(
                    (State: RideGameState.Initial, Frame: RideSceneFrame.Idle, Sample: (RideSceneSample?)null),
                    static (current, sample) =>
                    {
                        var state = RideGame.Advance(current.State, sample.Input);
                        var frame = RideSceneFrame.Create(state, sample.Input, sample.HeartRateZoneIndex, sample.HeartRateZones.Count, sample.SegmentRemainingSeconds);
                        return (State: state, Frame: frame, Sample: (RideSceneSample?)sample);
                    })
                .StartWith((State: RideGameState.Initial, Frame: RideSceneFrame.Idle, Sample: (RideSceneSample?)null)))
            .Switch()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Publish()
            .RefCount();

        _gameHelper = game.Select(static update => update.State)
            .ToProperty(this, static viewModel => viewModel.Game, RideGameState.Initial, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_gameHelper);
        _frameHelper = game.Select(static update => update.Frame)
            .CombineLatest(progress.ObserveOn(RxApp.MainThreadScheduler), static (frame, active) => active ? frame : RideSceneFrame.Idle)
            .ToProperty(this, static viewModel => viewModel.Frame, RideSceneFrame.Idle, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_frameHelper);
        _isRideInProgressHelper = progress.ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, static viewModel => viewModel.IsRideInProgress, false, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_isRideInProgressHelper);
        _hasSecondDisplayHelper = platform.DisplayCount
            .Select(static count => count > 1)
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, static viewModel => viewModel.HasSecondDisplay, false, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_hasSecondDisplayHelper);

        var gameState = this.WhenAnyValue(static viewModel => viewModel.Game);
        var latestSample = game.Select(static update => update.Sample);
        _elapsedTextHelper = gameState.Select(static state => FormatSeconds(state.RideSeconds))
            .ToProperty(this, static viewModel => viewModel.ElapsedText, "0:00", scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_elapsedTextHelper);
        _distanceTextHelper = gameState.Select(static state => $"{state.DistanceMeters / 1_000d:0.00} km")
            .ToProperty(this, static viewModel => viewModel.DistanceText, "0.00 km", scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_distanceTextHelper);
        _nextDistanceTextHelper = gameState.Select(static state => GetNextDistanceText(state))
            .ToProperty(this, static viewModel => viewModel.NextDistanceText, GetNextDistanceText(RideGameState.Initial), scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_nextDistanceTextHelper);
        _nextDistanceProgressHelper = gameState.Select(static state => RideGame.DistanceMilestones.GetProgress(state.DistanceMeters))
            .ToProperty(this, static viewModel => viewModel.NextDistanceProgress, 0d, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_nextDistanceProgressHelper);
        _nextTimeTextHelper = gameState.Select(static state => GetNextTimeText(state))
            .ToProperty(this, static viewModel => viewModel.NextTimeText, GetNextTimeText(RideGameState.Initial), scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_nextTimeTextHelper);
        _nextTimeProgressHelper = gameState.Select(static state => RideGame.RideTimeMilestones.GetProgress(state.RideSeconds))
            .ToProperty(this, static viewModel => viewModel.NextTimeProgress, 0d, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_nextTimeProgressHelper);
        _streakTextHelper = gameState.Select(static state => state.TargetSeconds > 0d
                ? $"Streak {FormatSeconds(state.StreakSeconds)} · best {FormatSeconds(state.BestStreakSeconds)} · {state.OnTargetRatio:P0} on target"
                : string.Empty)
            .ToProperty(this, static viewModel => viewModel.StreakText, string.Empty, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_streakTextHelper);
        _heartRateTextHelper = latestSample.Select(static sample => sample?.Input.HeartRate is { } beats ? beats.ToString(CultureInfo.CurrentCulture) : "--")
            .ToProperty(this, static viewModel => viewModel.HeartRateText, "--", scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_heartRateTextHelper);
        _heartRateZoneTextHelper = latestSample.Select(static sample => GetZoneText(sample))
            .ToProperty(this, static viewModel => viewModel.HeartRateZoneText, "No heart-rate monitor", scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_heartRateZoneTextHelper);
        _heartRateZoneIndexHelper = latestSample.Select(static sample => sample?.HeartRateZoneIndex ?? -1)
            .ToProperty(this, static viewModel => viewModel.HeartRateZoneIndex, -1, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_heartRateZoneIndexHelper);
        _zonesHelper = latestSample
            .Select(static sample => sample is { } current
                ? (current.HeartRateZones, current.HeartRateZoneIndex, current.Input.Target?.MinimumHeartRate, current.Input.Target?.MaximumHeartRate)
                : ([], -1, null, null))
            .DistinctUntilChanged()
            .Select(static key => CreateZones(key.HeartRateZones, key.HeartRateZoneIndex, key.MinimumHeartRate, key.MaximumHeartRate))
            .ToProperty(this, static viewModel => viewModel.Zones, [], scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_zonesHelper);
        _metricsTextHelper = latestSample.Select(static sample => GetMetricsText(sample))
            .ToProperty(this, static viewModel => viewModel.MetricsText, string.Empty, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_metricsTextHelper);
        _statusTextHelper = this.WhenAnyValue(static viewModel => viewModel.Game, static viewModel => viewModel.IsRideInProgress, static (state, active) => GetStatusText(state, active))
            .ToProperty(this, static viewModel => viewModel.StatusText, "Start a ride", scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_statusTextHelper);
        _statusKindHelper = gameState.Select(static state => state.Status)
            .ToProperty(this, static viewModel => viewModel.StatusKind, RideGameTargetStatus.Idle, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_statusKindHelper);
        _medalsTextHelper = gameState.Select(static state => $"🥇 {state.GoldMedals}   🥈 {state.SilverMedals}   🥉 {state.BronzeMedals}")
            .ToProperty(this, static viewModel => viewModel.MedalsText, "🥇 0   🥈 0   🥉 0", scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_medalsTextHelper);
        _intervalTextHelper = game.Select(static update => GetIntervalText(update.State, update.Frame.SegmentRemainingSeconds))
            .ToProperty(this, static viewModel => viewModel.IntervalText, string.Empty, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_intervalTextHelper);
        _recentAchievementsHelper = gameState.Select(static state => state.Achievements)
            .DistinctUntilChanged()
            .Select(static achievements => (IReadOnlyList<string>)[.. achievements.TakeLast(RecentAchievementCount).Reverse().Select(static achievement => $"🏆 {achievement.Title}")])
            .ToProperty(this, static viewModel => viewModel.RecentAchievements, [], scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_recentAchievementsHelper);
        _achievementCountTextHelper = gameState.Select(static state => state.Achievements.Count == 1 ? "1 achievement" : $"{state.Achievements.Count} achievements")
            .ToProperty(this, static viewModel => viewModel.AchievementCountText, "0 achievements", scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_achievementCountTextHelper);

        var achievements = gameState.Select(static state => state.LatestAchievement).DistinctUntilChanged();
        _achievementTextHelper = achievements.Where(static achievement => achievement is not null)
            .Select(static achievement => $"🏆 {achievement!.Value.Title}")
            .ToProperty(this, static viewModel => viewModel.AchievementText, string.Empty, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_achievementTextHelper);
        _isAchievementVisibleHelper = achievements
            .Select(achievement => achievement is null
                ? Observable.Return(false)
                : Observable.Return(true).Concat(Observable.Return(false).Delay(AchievementDisplayDuration, timerScheduler)))
            .Switch()
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToProperty(this, static viewModel => viewModel.IsAchievementVisible, false, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_isAchievementVisibleHelper);

        subscriptions.Add(progress.Where(static active => active)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => IsDismissedForRide = false));
        _shouldShowWindowHelper = this.WhenAnyValue(
                static viewModel => viewModel.IsWindowRequested,
                static viewModel => viewModel.AutoOpenOnSecondDisplay,
                static viewModel => viewModel.IsDismissedForRide,
                static viewModel => viewModel.IsRideInProgress,
                static viewModel => viewModel.HasSecondDisplay,
                static (requested, auto, dismissed, active, second) => requested || (auto && active && second && !dismissed))
            .DistinctUntilChanged()
            .ToProperty(this, static viewModel => viewModel.ShouldShowWindow, false, scheduler: ImmediateScheduler.Instance);
        subscriptions.Add(_shouldShowWindowHelper);
    }

    [ObservableAsProperty(InitialValue = "global::Stationary.Ftms.Dashboard.Core.RideGameState.Initial")]
    public partial RideGameState Game { get; }

    [ObservableAsProperty(InitialValue = "global::Stationary.Ftms.Dashboard.Core.RideSceneFrame.Idle")]
    public partial RideSceneFrame Frame { get; }

    [ObservableAsProperty]
    public partial bool IsRideInProgress { get; }

    [ObservableAsProperty]
    public partial bool HasSecondDisplay { get; }

    [ObservableAsProperty]
    public partial bool ShouldShowWindow { get; }

    [ObservableAsProperty(InitialValue = "\"0:00\"")]
    public partial string ElapsedText { get; }

    [ObservableAsProperty(InitialValue = "\"0.00 km\"")]
    public partial string DistanceText { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string NextDistanceText { get; }

    [ObservableAsProperty]
    public partial double NextDistanceProgress { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string NextTimeText { get; }

    [ObservableAsProperty]
    public partial double NextTimeProgress { get; }

    [ObservableAsProperty(InitialValue = "\"--\"")]
    public partial string HeartRateText { get; }

    [ObservableAsProperty(InitialValue = "\"No heart-rate monitor\"")]
    public partial string HeartRateZoneText { get; }

    [ObservableAsProperty(InitialValue = "-1")]
    public partial int HeartRateZoneIndex { get; }

    [ObservableAsProperty(InitialValue = "[]")]
    public partial IReadOnlyList<RideSceneZone> Zones { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string MetricsText { get; }

    [ObservableAsProperty(InitialValue = "[]")]
    public partial IReadOnlyList<string> RecentAchievements { get; }

    [ObservableAsProperty(InitialValue = "\"0 achievements\"")]
    public partial string AchievementCountText { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string StreakText { get; }

    [ObservableAsProperty(InitialValue = "\"Start a ride\"")]
    public partial string StatusText { get; }

    [ObservableAsProperty]
    public partial RideGameTargetStatus StatusKind { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string MedalsText { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string IntervalText { get; }

    [ObservableAsProperty(InitialValue = "string.Empty")]
    public partial string AchievementText { get; }

    [ObservableAsProperty]
    public partial bool IsAchievementVisible { get; }

    [Reactive]
    public partial bool AutoOpenOnSecondDisplay { get; private set; }

    [Reactive]
    public partial bool IsWindowRequested { get; private set; }

    [Reactive]
    public partial bool IsDismissedForRide { get; private set; }

    public void Dispose() => subscriptions.Dispose();

    [ReactiveCommand]
    private void OpenWindow()
    {
        IsWindowRequested = true;
        IsDismissedForRide = false;
    }

    // Also invoked when the user closes the scene window, so auto-open does not reopen it during the same ride.
    [ReactiveCommand]
    private void CloseWindow()
    {
        IsWindowRequested = false;
        IsDismissedForRide = IsRideInProgress;
    }

    [ReactiveCommand]
    private void SetAutoOpenOnSecondDisplay(bool enabled)
    {
        platform.SetPreference(AutoOpenPreferenceKey, enabled);
        AutoOpenOnSecondDisplay = enabled;
    }

    private static string GetStatusText(RideGameState state, bool active) => !active
        ? state.LastTimestamp is null ? "Start a ride" : $"Ride over · {state.Achievements.Count} achievements"
        : state.Status switch
        {
            RideGameTargetStatus.OnTarget => "On target — keep it steady",
            RideGameTargetStatus.TooLow => "Below target — push a little",
            RideGameTargetStatus.TooHigh => "Above target — ease off",
            RideGameTargetStatus.Riding => "Free ride — enjoy it",
            _ => "Paused",
        };

    private static string GetIntervalText(RideGameState state, double? remainingSeconds) => state.SegmentIndex < 0 || state.SegmentSeconds < 1d
        ? string.Empty
        : $"{state.SegmentName}{(remainingSeconds is { } remaining ? $" · {FormatSeconds(remaining)} left" : string.Empty)} · {state.SegmentOnTargetRatio:P0} on target · {state.ProjectedMedal switch
        {
            RideGameMedal.Gold => "gold pace",
            RideGameMedal.Silver => "silver pace",
            RideGameMedal.Bronze => "bronze pace",
            _ => "no medal yet",
        }}";

    private static string GetNextDistanceText(RideGameState state)
    {
        var next = state.NextDistanceMilestoneMeters;
        return $"Next {RideGame.FormatDistance(next)} · {(next - state.DistanceMeters) / 1_000d:0.00} km to go";
    }

    private static string GetNextTimeText(RideGameState state)
    {
        var next = state.NextRideTimeMilestoneSeconds;
        return $"Next {RideGame.FormatDuration(next)} · {FormatSeconds(next - state.RideSeconds)} to go";
    }

    private static string GetZoneText(RideSceneSample? sample) => sample switch
    {
        null or { HeartRateZones.Count: 0 } => "Set up heart-rate zones",
        { Input.HeartRate: null } => "No heart-rate signal",
        { HeartRateZoneIndex: var index, HeartRateZones: var zones } when index >= 0 && index < zones.Count => $"{zones[index].Code} · {zones[index].Name}",
        _ => string.Empty,
    };

    private static string GetMetricsText(RideSceneSample? sample) => sample?.Input is not { } input
        ? string.Empty
        : string.Create(
            CultureInfo.CurrentCulture,
            $"{(input.PowerWatts is { } watts ? $"{watts:0} W" : "-- W")}   {(input.CadenceRpm is { } cadence ? $"{cadence:0} rpm" : "-- rpm")}   {input.SpeedKilometersPerHour:0.0} km/h");

    private static IReadOnlyList<RideSceneZone> CreateZones(IReadOnlyList<HeartRateZone> zones, int currentIndex, ushort? minimumTarget, ushort? maximumTarget)
    {
        var targeted = minimumTarget is not null || maximumTarget is not null;
        return [.. zones.Select((zone, index) => new RideSceneZone(
            index,
            zones.Count,
            zone.Code,
            zone.Name,
            zone.MaximumBeatsPerMinute is { } maximum ? $"{zone.MinimumBeatsPerMinute}-{maximum}" : $"{zone.MinimumBeatsPerMinute}+",
            index == currentIndex,
            targeted
                && (maximumTarget is not { } high || zone.MinimumBeatsPerMinute <= high)
                && (minimumTarget is not { } low || zone.MaximumBeatsPerMinute is not { } top || top >= low)))];
    }

    private static string FormatSeconds(double seconds)
    {
        var time = TimeSpan.FromSeconds(double.Floor(seconds));
        return time.TotalHours >= 1d ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}