using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingWorkoutRunnerTests
{
    [Test]
    public async Task Runner_DoesNotStartOrAdvanceUntilTheBikeAcceptsTheTarget()
    {
        var start = DateTimeOffset.UnixEpoch;
        var applying = new TrainingWorkoutRunner(CreateWorkout()).Start();
        var advancedWhileApplying = applying.Advance(start.AddMinutes(2));
        var running = applying.RejectTarget().RetryTarget().ConfirmTargetApplied(start.AddMinutes(2));
        var beforeDuration = running.Advance(start.AddMinutes(2).AddSeconds(59));
        var atDuration = running.Advance(start.AddMinutes(3));

        using (Assert.Multiple())
        {
            await Assert.That(advancedWhileApplying).IsSameReferenceAs(applying);
            await Assert.That(beforeDuration).IsSameReferenceAs(running);
            await Assert.That(atDuration.State).IsEqualTo(TrainingWorkoutState.Completed);
            await Assert.That(running.State).IsEqualTo(TrainingWorkoutState.Running);
        }
    }

    [Test]
    public async Task Compile_RoundsTargetsTheBikeCannotSetExactly()
    {
        var definition = new TrainingWorkout(
            "test",
            "Test",
            "Test workout",
            TrainingObjective.Endurance,
            TrainingExperienceLevel.Beginner,
            [new("Work", TimeSpan.FromMinutes(1), 83d, null, null, "Ride", true)]);

        var compiled = TrainingWorkoutCompiler.Compile(definition, 200, new(0, 400, 5));

        await Assert.That(compiled.Segments[0].TargetWatts).IsEqualTo(165);
    }

    [Test]
    public async Task Compile_UsesUnroundedFtpRelativePowerWhenSelectingDeviceStep()
    {
        var definition = new TrainingWorkout(
            "test",
            "Test",
            "Test workout",
            TrainingObjective.Endurance,
            TrainingExperienceLevel.Beginner,
            [new("Work", TimeSpan.FromMinutes(1), 52d, null, null, "Ride", true)]);

        var compiled = TrainingWorkoutCompiler.Compile(definition, 201, new(50, 400, 10));

        await Assert.That(compiled.Segments[0].TargetWatts).IsEqualTo(100);
    }

    [Test]
    public async Task Compile_RejectsNotANumberFtpPercentage()
    {
        var definition = new TrainingWorkout(
            "test",
            "Test",
            "Test workout",
            TrainingObjective.Endurance,
            TrainingExperienceLevel.Beginner,
            [new("Work", TimeSpan.FromMinutes(1), double.NaN, null, null, "Ride", true)]);

        await Assert.That(() => TrainingWorkoutCompiler.Compile(definition, 200, new(0, 400, 5)))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(170, 400)]
    [Arguments(0, 160)]
    public async Task Compile_RejectsTargetsOutsideTheBikeRange(int minimum, int maximum)
    {
        var definition = new TrainingWorkout(
            "test",
            "Test",
            "Test workout",
            TrainingObjective.Endurance,
            TrainingExperienceLevel.Beginner,
            [new("Work", TimeSpan.FromMinutes(1), 83d, null, null, "Ride", true)]);

        await Assert.That(() => TrainingWorkoutCompiler.Compile(definition, 200, new(minimum, maximum, 5)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Runner_PauseAndResumeExcludePausedTime()
    {
        var start = DateTimeOffset.UnixEpoch;
        var runner = new TrainingWorkoutRunner(CreateWorkout())
            .Start()
            .ConfirmTargetApplied(start)
            .Pause(start.AddSeconds(30))
            .Resume(start.AddMinutes(1).AddSeconds(30));
        var advanced = runner.Advance(start.AddMinutes(2));

        await Assert.That(advanced.State).IsEqualTo(TrainingWorkoutState.Completed);
    }

    [Test]
    public async Task Runner_ReportsRemainingIntervalTimeAcrossPause()
    {
        var start = DateTimeOffset.UnixEpoch;
        var paused = new TrainingWorkoutRunner(CreateWorkout())
            .Start()
            .ConfirmTargetApplied(start)
            .Pause(start.AddSeconds(30));
        var remainingWhilePaused = paused.GetCurrentSegmentRemaining(start.AddMinutes(2));
        var runner = paused.Resume(start.AddMinutes(2));
        var remainingAfterResuming = runner.GetCurrentSegmentRemaining(start.AddMinutes(2).AddSeconds(15));

        using (Assert.Multiple())
        {
            await Assert.That(runner.SegmentCount).IsEqualTo(1);
            await Assert.That(remainingWhilePaused).IsEqualTo(TimeSpan.FromSeconds(30));
            await Assert.That(remainingAfterResuming).IsEqualTo(TimeSpan.FromSeconds(15));
        }
    }

    private static CompiledTrainingWorkout CreateWorkout()
    {
        var definition = new TrainingWorkout(
            "test",
            "Test",
            "Test workout",
            TrainingObjective.Endurance,
            TrainingExperienceLevel.Beginner,
            [new("Work", TimeSpan.FromMinutes(1), 80d, null, null, "Ride", true)]);

        return TrainingWorkoutCompiler.Compile(definition, 200, new(0, 400, 5));
    }
}