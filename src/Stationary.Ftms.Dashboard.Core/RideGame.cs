using System.Collections.Immutable;

namespace Stationary.Ftms.Dashboard.Core;

public enum RideGameTargetStatus
{
    Idle,
    Riding,
    OnTarget,
    TooLow,
    TooHigh,
}

public enum RideGameMedal
{
    None,
    Bronze,
    Silver,
    Gold,
}

public enum RideGameAchievementKind
{
    Distance,
    RideTime,
    TimeOnTarget,
    Streak,
    GoldIntervals,
    WorkoutComplete,
}

// Milestone is metres for distance, seconds for times and streaks, and a count for gold intervals.
public readonly record struct RideGameAchievement(RideGameAchievementKind Kind, int Milestone, string Title, DateTimeOffset AchievedAt);

// Milestones every FirstStep up to Until, then every LaterStep; Until must be a multiple of both steps.
public readonly record struct RideGameMilestoneSchedule(double FirstStep, double Until, double LaterStep)
{
    public double GetReached(double value) => value < FirstStep
        ? 0d
        : value < Until ? double.Floor(value / FirstStep) * FirstStep : double.Floor(value / LaterStep) * LaterStep;

    public double GetNext(double value) => value < Until
        ? (double.Floor(value / FirstStep) + 1d) * FirstStep
        : (double.Floor(value / LaterStep) + 1d) * LaterStep;

    public double GetProgress(double value)
    {
        var previous = GetReached(value);
        var next = GetNext(value);
        return double.Clamp((value - previous) / (next - previous), 0d, 1d);
    }
}

public readonly record struct RideGameIntervalMedal(int SegmentIndex, string SegmentName, RideGameMedal Medal, double OnTargetRatio);

// Bounds of the current interval; any null bound is not scored.
public readonly record struct RideGameTarget(
    string Name,
    int? MinimumWatts,
    int? MaximumWatts,
    ushort? MinimumHeartRate,
    ushort? MaximumHeartRate,
    int? MinimumCadenceRpm,
    int? MaximumCadenceRpm)
{
    public bool HasBounds => MinimumWatts is not null || MaximumWatts is not null || MinimumHeartRate is not null
        || MaximumHeartRate is not null || MinimumCadenceRpm is not null || MaximumCadenceRpm is not null;
}

public readonly record struct RideGameInput(
    DateTimeOffset Timestamp,
    bool IsRunning,
    double? PowerWatts,
    double? CadenceRpm,
    ushort? HeartRate,
    double SpeedKilometersPerHour,
    RideGameTarget? Target,
    int SegmentIndex,
    bool IsWorkoutComplete);

public sealed record RideGameState
{
    public static RideGameState Initial { get; } = new();

    public DateTimeOffset? LastTimestamp { get; init; }

    public RideGameTargetStatus Status { get; init; }

    // Running time, including time spent below target.
    public double RideSeconds { get; init; }

    public double DistanceMeters { get; init; }

    public double StreakSeconds { get; init; }

    public double BestStreakSeconds { get; init; }

    public double OffTargetSeconds { get; init; }

    // Only intervals with a target count toward target time.
    public double TargetSeconds { get; init; }

    public double OnTargetSeconds { get; init; }

    public int SegmentIndex { get; init; } = -1;

    public string SegmentName { get; init; } = string.Empty;

    public double SegmentSeconds { get; init; }

    public double SegmentOnTargetSeconds { get; init; }

    public bool IsWorkoutComplete { get; init; }

    public ImmutableList<RideGameIntervalMedal> Medals { get; init; } = [];

    public ImmutableList<RideGameAchievement> Achievements { get; init; } = [];

    public double SegmentOnTargetRatio => SegmentSeconds > 0d ? SegmentOnTargetSeconds / SegmentSeconds : 0d;

    public double OnTargetRatio => TargetSeconds > 0d ? OnTargetSeconds / TargetSeconds : 0d;

    public RideGameMedal ProjectedMedal => RideGame.GetMedal(SegmentOnTargetRatio);

    public RideGameAchievement? LatestAchievement => Achievements.IsEmpty ? null : Achievements[^1];

    public RideGameIntervalMedal? LatestMedal => Medals.IsEmpty ? null : Medals[^1];

    public int GoldMedals => Medals.Count(static medal => medal.Medal == RideGameMedal.Gold);

    public int SilverMedals => Medals.Count(static medal => medal.Medal == RideGameMedal.Silver);

    public int BronzeMedals => Medals.Count(static medal => medal.Medal == RideGameMedal.Bronze);

    public double NextDistanceMilestoneMeters => RideGame.DistanceMilestones.GetNext(DistanceMeters);

    public double NextRideTimeMilestoneSeconds => RideGame.RideTimeMilestones.GetNext(RideSeconds);
}

// Turns 1 Hz ride samples into session achievements: distance and ride-time milestones, time on target and unbroken
// streaks during targeted intervals, interval medals by the share of time on target, and completing the workout.
public static class RideGame
{
    public const double PowerTolerance = 0.1d;
    public const int HeartRateToleranceBeatsPerMinute = 2;
    public const int CadenceToleranceRpm = 3;
    public static readonly TimeSpan StreakGrace = TimeSpan.FromSeconds(5);
    public static readonly RideGameMilestoneSchedule DistanceMilestones = new(1_000d, 5_000d, 5_000d);
    public static readonly RideGameMilestoneSchedule RideTimeMilestones = new(300d, 1_800d, 900d);
    public static readonly RideGameMilestoneSchedule TimeOnTargetMilestones = new(300d, 1_800d, 600d);
    public static readonly RideGameMilestoneSchedule StreakMilestones = new(120d, 600d, 300d);
    private static readonly TimeSpan MaximumStep = TimeSpan.FromSeconds(5);

    public static RideGameMedal GetMedal(double onTargetRatio) => onTargetRatio switch
    {
        >= 0.9d => RideGameMedal.Gold,
        >= 0.75d => RideGameMedal.Silver,
        >= 0.5d => RideGameMedal.Bronze,
        _ => RideGameMedal.None,
    };

    public static RideGameTargetStatus Evaluate(RideGameInput input)
    {
        var pedalling = input.CadenceRpm > 0d || input.PowerWatts > 0d;
        if (input.Target is not { HasBounds: true } target)
        {
            return pedalling ? RideGameTargetStatus.OnTarget : RideGameTargetStatus.Idle;
        }

        if (!pedalling)
        {
            return RideGameTargetStatus.TooLow;
        }

        // Heart rate leads when the interval targets it; power and cadence follow.
        var heartRate = Compare(input.HeartRate, target.MinimumHeartRate, target.MaximumHeartRate, HeartRateToleranceBeatsPerMinute);
        if (heartRate != RideGameTargetStatus.OnTarget)
        {
            return heartRate;
        }

        var powerSlack = (target.MaximumWatts ?? target.MinimumWatts ?? 0) * PowerTolerance;
        var power = Compare(input.PowerWatts, target.MinimumWatts, target.MaximumWatts, powerSlack);
        if (power != RideGameTargetStatus.OnTarget)
        {
            return power;
        }

        return Compare(input.CadenceRpm, target.MinimumCadenceRpm, target.MaximumCadenceRpm, CadenceToleranceRpm);
    }

    public static RideGameState Advance(RideGameState state, RideGameInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        var step = state.LastTimestamp is { } last && input.Timestamp > last
            ? input.Timestamp - last
            : TimeSpan.Zero;
        var seconds = (step > MaximumStep ? MaximumStep : step).TotalSeconds;
        var next = state with { LastTimestamp = input.Timestamp };
        next = AdvanceSegment(next, input);
        if (!input.IsRunning)
        {
            return CompleteWorkout(next with { Status = RideGameTargetStatus.Idle }, input);
        }

        var status = Evaluate(input);
        var onTarget = status == RideGameTargetStatus.OnTarget;
        var targeted = input.Target is { HasBounds: true };
        var offTarget = onTarget ? 0d : next.OffTargetSeconds + seconds;
        var streak = onTarget
            ? next.StreakSeconds + seconds
            : offTarget > StreakGrace.TotalSeconds ? 0d : next.StreakSeconds;
        next = next with
        {
            Status = targeted || !onTarget ? status : RideGameTargetStatus.Riding,
            RideSeconds = next.RideSeconds + seconds,
            DistanceMeters = next.DistanceMeters + (double.Max(input.SpeedKilometersPerHour, 0d) / 3.6d * seconds),
            OffTargetSeconds = offTarget,
            StreakSeconds = streak,
            BestStreakSeconds = double.Max(next.BestStreakSeconds, streak),
            TargetSeconds = next.TargetSeconds + (targeted ? seconds : 0d),
            OnTargetSeconds = next.OnTargetSeconds + (targeted && onTarget ? seconds : 0d),
            SegmentSeconds = next.SegmentSeconds + (targeted ? seconds : 0d),
            SegmentOnTargetSeconds = next.SegmentOnTargetSeconds + (targeted && onTarget ? seconds : 0d),
        };
        next = targeted ? AwardTargets(next, input.Timestamp) : next;
        return CompleteWorkout(Award(next, input.Timestamp), input);
    }

    public static string FormatDuration(double seconds)
    {
        var minutes = (int)double.Round(seconds / 60d);
        return minutes switch
        {
            < 60 => $"{minutes} min",
            _ when minutes % 60 == 0 => $"{minutes / 60} h",
            _ => $"{minutes / 60} h {minutes % 60} min",
        };
    }

    public static string FormatDistance(double meters) => $"{meters / 1_000d:0.#} km";

    private static RideGameTargetStatus Compare(double? value, double? minimum, double? maximum, double slack)
    {
        if (minimum is null && maximum is null)
        {
            return RideGameTargetStatus.OnTarget;
        }

        if (value is not { } actual)
        {
            return RideGameTargetStatus.TooLow;
        }

        if (minimum is { } low && actual < low - slack)
        {
            return RideGameTargetStatus.TooLow;
        }

        return maximum is { } high && actual > high + slack ? RideGameTargetStatus.TooHigh : RideGameTargetStatus.OnTarget;
    }

    private static RideGameState AdvanceSegment(RideGameState state, RideGameInput input)
    {
        if (input.SegmentIndex == state.SegmentIndex)
        {
            return state;
        }

        var closed = CloseSegment(state, input.Timestamp);
        return closed with
        {
            SegmentIndex = input.SegmentIndex,
            SegmentName = input.Target?.Name ?? string.Empty,
            SegmentSeconds = 0d,
            SegmentOnTargetSeconds = 0d,
        };
    }

    private static RideGameState CloseSegment(RideGameState state, DateTimeOffset timestamp)
    {
        if (state.SegmentIndex < 0 || state.SegmentSeconds < 1d)
        {
            return state;
        }

        var ratio = state.SegmentOnTargetRatio;
        var closed = state with
        {
            Medals = state.Medals.Add(new(state.SegmentIndex, state.SegmentName, GetMedal(ratio), ratio)),
            SegmentSeconds = 0d,
            SegmentOnTargetSeconds = 0d,
        };
        return Award(closed, timestamp);
    }

    private static RideGameState CompleteWorkout(RideGameState state, RideGameInput input)
    {
        if (!input.IsWorkoutComplete || state.IsWorkoutComplete)
        {
            return state;
        }

        var closed = CloseSegment(state, input.Timestamp) with { IsWorkoutComplete = true, Status = RideGameTargetStatus.Idle };
        return Unlock(closed, RideGameAchievementKind.WorkoutComplete, 1, "Workout complete", input.Timestamp);
    }

    private static RideGameState Award(RideGameState state, DateTimeOffset timestamp)
    {
        var distance = (int)DistanceMilestones.GetReached(state.DistanceMeters);
        var time = (int)RideTimeMilestones.GetReached(state.RideSeconds);
        var golds = state.GoldMedals;
        var next = distance > 0 ? Unlock(state, RideGameAchievementKind.Distance, distance, $"{FormatDistance(distance)} ridden", timestamp) : state;
        next = time > 0 ? Unlock(next, RideGameAchievementKind.RideTime, time, $"{FormatDuration(time)} of riding", timestamp) : next;
        return golds is 1 or 3 or 5 or 10
            ? Unlock(next, RideGameAchievementKind.GoldIntervals, golds, golds == 1 ? "First gold interval" : $"{golds} gold intervals", timestamp)
            : next;
    }

    private static RideGameState AwardTargets(RideGameState state, DateTimeOffset timestamp)
    {
        var onTarget = (int)TimeOnTargetMilestones.GetReached(state.OnTargetSeconds);
        var streak = (int)StreakMilestones.GetReached(state.StreakSeconds);
        var next = onTarget > 0 ? Unlock(state, RideGameAchievementKind.TimeOnTarget, onTarget, $"{FormatDuration(onTarget)} on target", timestamp) : state;
        return streak > 0 ? Unlock(next, RideGameAchievementKind.Streak, streak, $"{FormatDuration(streak)} unbroken streak", timestamp) : next;
    }

    private static RideGameState Unlock(RideGameState state, RideGameAchievementKind kind, int milestone, string title, DateTimeOffset timestamp) =>
        state.Achievements.Any(achievement => achievement.Kind == kind && achievement.Milestone == milestone)
            ? state
            : state with { Achievements = state.Achievements.Add(new(kind, milestone, title, timestamp)) };
}

// One snapshot of the ride for the 3D scene; the renderer interpolates motion between these 1 Hz frames.
public sealed record RideSceneFrame(
    bool IsRunning,
    double SpeedKilometersPerHour,
    double CadenceRpm,
    double PowerWatts,
    int? HeartRate,
    RideGameTargetStatus Status,
    bool HasTarget,
    double StreakSeconds,
    double DistanceMeters,
    double NextDistanceMilestoneMeters,
    int HeartRateZoneIndex,
    int HeartRateZoneCount,
    RideGameMedal ProjectedMedal,
    double? SegmentRemainingSeconds,
    int AchievementCount,
    int MedalCount,
    RideGameMedal LatestMedal)
{
    public static RideSceneFrame Idle { get; } = new(
        false, 0d, 0d, 0d, null, RideGameTargetStatus.Idle, false, 0d, 0d, RideGame.DistanceMilestones.FirstStep, -1, 0, RideGameMedal.None, null, 0, 0, RideGameMedal.None);

    public static RideSceneFrame Create(RideGameState state, RideGameInput input, int heartRateZoneIndex, int heartRateZoneCount, double? segmentRemainingSeconds)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(
            input.IsRunning,
            input.IsRunning ? double.Max(input.SpeedKilometersPerHour, 0d) : 0d,
            input.IsRunning ? input.CadenceRpm ?? 0d : 0d,
            input.PowerWatts ?? 0d,
            input.HeartRate,
            state.Status,
            input.Target is { HasBounds: true },
            state.StreakSeconds,
            state.DistanceMeters,
            state.NextDistanceMilestoneMeters,
            heartRateZoneIndex,
            heartRateZoneCount,
            state.ProjectedMedal,
            segmentRemainingSeconds,
            state.Achievements.Count,
            state.Medals.Count,
            state.LatestMedal?.Medal ?? RideGameMedal.None);
    }
}