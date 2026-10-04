using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingWorkoutRunnerTests
{
    [Test]
    public async Task Runner_DoesNotStartOrAdvanceUntilTheBikeAcceptsTheTarget()
    {
        var start = DateTimeOffset.UnixEpoch;
        var runner = new TrainingWorkoutRunner(CreateWorkout());

        runner.Start();
        var advancedWhileApplying = runner.Advance(start.AddMinutes(2));
        runner.RejectTarget();
        runner.RetryTarget();
        runner.ConfirmTargetApplied(start.AddMinutes(2));
        var advancedBeforeDuration = runner.Advance(start.AddMinutes(2).AddSeconds(59));
        var advancedAtDuration = runner.Advance(start.AddMinutes(3));

        using (Assert.Multiple())
        {
            await Assert.That(advancedWhileApplying).IsFalse();
            await Assert.That(advancedBeforeDuration).IsFalse();
            await Assert.That(advancedAtDuration).IsTrue();
            await Assert.That(runner.State).IsEqualTo(TrainingWorkoutState.Completed);
        }
    }

    [Test]
    public async Task Compile_RejectsTargetsTheBikeCannotSetExactly()
    {
        var definition = new TrainingWorkout(
            "test",
            "Test",
            "Test workout",
            TrainingObjective.Endurance,
            TrainingExperienceLevel.Beginner,
            [new("Work", TimeSpan.FromMinutes(1), 83d, null, null, "Ride", true)]);

        await Assert.That(() => TrainingWorkoutCompiler.Compile(definition, 200, new(0, 400, 5)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Runner_PauseAndResumeExcludePausedTime()
    {
        var start = DateTimeOffset.UnixEpoch;
        var runner = new TrainingWorkoutRunner(CreateWorkout());

        runner.Start();
        runner.ConfirmTargetApplied(start);
        runner.Pause(start.AddSeconds(30));
        runner.Resume(start.AddMinutes(1).AddSeconds(30));
        var advancedAtOneMinuteOfActiveTime = runner.Advance(start.AddMinutes(2));

        using (Assert.Multiple())
        {
            await Assert.That(advancedAtOneMinuteOfActiveTime).IsTrue();
            await Assert.That(runner.State).IsEqualTo(TrainingWorkoutState.Completed);
        }
    }

    [Test]
    public async Task Runner_ReportsRemainingIntervalTimeAcrossPause()
    {
        var start = DateTimeOffset.UnixEpoch;
        var runner = new TrainingWorkoutRunner(CreateWorkout());

        runner.Start();
        runner.ConfirmTargetApplied(start);
        runner.Pause(start.AddSeconds(30));
        var remainingWhilePaused = runner.GetCurrentSegmentRemaining(start.AddMinutes(2));
        runner.Resume(start.AddMinutes(2));
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