using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class RideGameTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 14, 30, 0, TimeSpan.Zero);
    private static readonly RideGameTarget Endurance = new("Endurance", 140, 140, 115, 130, 80, 95);

    [Test]
    public async Task Evaluate_RanksHeartRateBeforePower()
    {
        var onTarget = Input(0, power: 140d, cadence: 88d, heartRate: 120);

        using (Assert.Multiple())
        {
            await Assert.That(RideGame.Evaluate(onTarget)).IsEqualTo(RideGameTargetStatus.OnTarget);
            await Assert.That(RideGame.Evaluate(onTarget with { HeartRate = 140 })).IsEqualTo(RideGameTargetStatus.TooHigh);
            await Assert.That(RideGame.Evaluate(onTarget with { HeartRate = 140, PowerWatts = 100d })).IsEqualTo(RideGameTargetStatus.TooHigh);
            await Assert.That(RideGame.Evaluate(onTarget with { PowerWatts = 152d })).IsEqualTo(RideGameTargetStatus.OnTarget);
            await Assert.That(RideGame.Evaluate(onTarget with { PowerWatts = 120d })).IsEqualTo(RideGameTargetStatus.TooLow);
            await Assert.That(RideGame.Evaluate(onTarget with { CadenceRpm = 105d })).IsEqualTo(RideGameTargetStatus.TooHigh);
            await Assert.That(RideGame.Evaluate(onTarget with { CadenceRpm = 0d, PowerWatts = 0d })).IsEqualTo(RideGameTargetStatus.TooLow);
            await Assert.That(RideGame.Evaluate(onTarget with { Target = null })).IsEqualTo(RideGameTargetStatus.OnTarget);
        }
    }

    [Test]
    public async Task Advance_UnlocksStreakAndTimeOnTargetMilestones()
    {
        var state = Ride(301, static second => Input(second, power: 140d, cadence: 88d, heartRate: 120));
        var titles = state.Achievements.Select(static achievement => achievement.Title).ToList();

        using (Assert.Multiple())
        {
            await Assert.That(state.StreakSeconds).IsEqualTo(300d);
            await Assert.That(state.OnTargetRatio).IsEqualTo(1d);
            await Assert.That(titles).Contains("2 min unbroken streak");
            await Assert.That(titles).Contains("4 min unbroken streak");
            await Assert.That(titles).Contains("5 min on target");
            await Assert.That(titles).Contains("5 min of riding");
        }
    }

    [Test]
    public async Task Advance_KeepsTheStreakThroughAShortDipButResetsAfterTheGrace()
    {
        var shortDip = Ride(70, static second => Input(second, power: second is >= 60 and < 64 ? 90d : 140d, cadence: 88d, heartRate: 120));
        var longDip = Ride(70, static second => Input(second, power: second is >= 60 and < 68 ? 90d : 140d, cadence: 88d, heartRate: 120));

        using (Assert.Multiple())
        {
            await Assert.That(shortDip.StreakSeconds).IsEqualTo(65d);
            await Assert.That(longDip.StreakSeconds).IsEqualTo(2d);
            await Assert.That(longDip.BestStreakSeconds).IsEqualTo(59d);
        }
    }

    [Test]
    public async Task Advance_AwardsAMedalWhenTheIntervalChanges()
    {
        var target = Endurance;
        var state = Ride(101, second => Input(
            second,
            power: second < 50 && second % 10 == 0 ? 60d : 140d,
            cadence: 88d,
            heartRate: 120,
            target: second < 50 ? target : target with { Name = "Recovery" },
            segment: second < 50 ? 0 : 1));

        await Assert.That(state.Medals).IsEquivalentTo([new RideGameIntervalMedal(0, "Endurance", RideGameMedal.Gold, 45d / 49d)]);
    }

    [Test]
    public async Task Advance_UnlocksDistanceMilestonesWhetherOrNotOnTarget()
    {
        var offTarget = Ride(201, static second => Input(second, power: 50d, cadence: 88d, heartRate: 120, speed: 36d));

        using (Assert.Multiple())
        {
            await Assert.That(offTarget.DistanceMeters).IsEqualTo(2_000d).Within(0.001d);
            await Assert.That(offTarget.Achievements.Where(static achievement => achievement.Kind == RideGameAchievementKind.Distance).Select(static achievement => achievement.Title))
                .IsEquivalentTo(["1 km ridden", "2 km ridden"]);
            await Assert.That(offTarget.Achievements.Any(static achievement => achievement.Kind is RideGameAchievementKind.Streak or RideGameAchievementKind.TimeOnTarget)).IsFalse();
            await Assert.That(offTarget.NextDistanceMilestoneMeters).IsEqualTo(3_000d).Within(0.001d);
        }
    }

    [Test]
    public async Task Advance_DoesNotCountWhilePausedAndCompletesOnce()
    {
        var state = Ride(31, static second => Input(second, power: 140d, cadence: 88d, heartRate: 120) with { IsRunning = second < 10 });
        var completed = RideGame.Advance(state, Input(31, power: 140d, cadence: 88d, heartRate: 120) with { IsRunning = false, IsWorkoutComplete = true });
        var again = RideGame.Advance(completed, Input(32, power: 140d, cadence: 88d, heartRate: 120) with { IsRunning = false, IsWorkoutComplete = true });

        using (Assert.Multiple())
        {
            await Assert.That(state.OnTargetSeconds).IsEqualTo(9d);
            await Assert.That(state.RideSeconds).IsEqualTo(9d);
            await Assert.That(state.Status).IsEqualTo(RideGameTargetStatus.Idle);
            await Assert.That(completed.IsWorkoutComplete).IsTrue();
            await Assert.That(completed.Medals.Count).IsEqualTo(1);
            await Assert.That(again.Achievements.Count(static achievement => achievement.Kind == RideGameAchievementKind.WorkoutComplete)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Advance_CountsRideTimeButNotTargetTimeOnAFreeRide()
    {
        var state = Ride(601, static second => Input(second, power: 150d, cadence: 85d, heartRate: null, target: null, segment: -1));

        using (Assert.Multiple())
        {
            await Assert.That(state.Status).IsEqualTo(RideGameTargetStatus.Riding);
            await Assert.That(state.RideSeconds).IsEqualTo(600d);
            await Assert.That(state.TargetSeconds).IsEqualTo(0d);
            await Assert.That(state.Medals).IsEmpty();
            await Assert.That(state.Achievements.Where(static achievement => achievement.Kind == RideGameAchievementKind.RideTime).Select(static achievement => achievement.Milestone))
                .IsEquivalentTo([300, 600]);
        }
    }

    [Test]
    [Arguments(0d, 0d, 1_000d)]
    [Arguments(4_999d, 4_000d, 5_000d)]
    [Arguments(5_000d, 5_000d, 10_000d)]
    [Arguments(12_300d, 10_000d, 15_000d)]
    public async Task DistanceMilestones_StepByKilometreThenByFive(double meters, double reached, double next)
    {
        using (Assert.Multiple())
        {
            await Assert.That(RideGame.DistanceMilestones.GetReached(meters)).IsEqualTo(reached);
            await Assert.That(RideGame.DistanceMilestones.GetNext(meters)).IsEqualTo(next);
        }
    }

    [Test]
    [Arguments(300d, "5 min")]
    [Arguments(3_600d, "1 h")]
    [Arguments(4_500d, "1 h 15 min")]
    public async Task FormatDuration_UsesMinutesAndHours(double seconds, string text) =>
        await Assert.That(RideGame.FormatDuration(seconds)).IsEqualTo(text);

    [Test]
    [Arguments(0.95d, RideGameMedal.Gold)]
    [Arguments(0.8d, RideGameMedal.Silver)]
    [Arguments(0.5d, RideGameMedal.Bronze)]
    [Arguments(0.2d, RideGameMedal.None)]
    public async Task GetMedal_UsesTheOnTargetRatio(double ratio, RideGameMedal medal) =>
        await Assert.That(RideGame.GetMedal(ratio)).IsEqualTo(medal);

    private static RideGameState Ride(int samples, Func<int, RideGameInput> input)
    {
        var state = RideGameState.Initial;
        for (var second = 0; second < samples; second++)
        {
            state = RideGame.Advance(state, input(second));
        }

        return state;
    }

    private static RideGameInput Input(
        int second,
        double? power,
        double? cadence,
        ushort? heartRate,
        double speed = 30d,
        RideGameTarget? target = null,
        int segment = 0) =>
        new(Start.AddSeconds(second), true, power, cadence, heartRate, speed, target ?? (segment < 0 ? null : Endurance), segment, false);
}