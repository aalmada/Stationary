using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingSessionEngineTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static readonly TrainingPowerCapabilities Capabilities = new(50, 500, 10);
    private static readonly TrainingMachine Running = new(IsConnected: true, TrainingMachineState.Running);
    private static readonly TrainingTelemetry Pedalling = new(90, 150, null, null);
    private static readonly TrainingTelemetry Stopped = new(0, 0, null, null);

    private readonly TrainingSessionEngine engine = new(static _ => []);

    [Test]
    public async Task IdleTick_ReturnsTheSameStateWithoutEffects()
    {
        var transition = engine.Reduce(TrainingSession.Idle, new ClockTicked(Start, Pedalling));

        using (Assert.Multiple())
        {
            await Assert.That(transition.State).IsSameReferenceAs(TrainingSession.Idle);
            await Assert.That(transition.Effects).IsEmpty();
        }
    }

    [Test]
    public async Task Begin_RequestsTheRideAndTheMachineSession()
    {
        var transition = engine.Reduce(TrainingSession.Idle with { Machine = Running }, Begun(CreateWorkout()));

        using (Assert.Multiple())
        {
            await Assert.That(transition.State.IsStarting).IsTrue();
            await Assert.That(transition.State.IsActive).IsTrue();
            await Assert.That(transition.State.Machine).IsEqualTo(Running);
            await Assert.That(transition.Effects).IsEquivalentTo(new TrainingSessionEffect[]
            {
                new NavigateTo(TrainingDestination.Ride),
                new StartMachineSession(1, AnnounceWorkout: true),
            });
        }
    }

    [Test]
    public async Task Start_HoldsMinimumPowerUntilTheRiderPedalsThenAppliesTheSegmentTarget()
    {
        var begun = engine.Reduce(TrainingSession.Idle with { Machine = Running }, Begun(CreateWorkout()));
        var started = engine.Reduce(begun.State, new MachineSessionStarted(Start, Single<StartMachineSession>(begun), true, "Connected"));
        var hold = Single<ApplyPowerTarget>(started);
        var holding = engine.Reduce(started.State, new PowerTargetCompleted(Start, hold, true));
        var stillWaiting = engine.Reduce(holding.State, new ClockTicked(Start.AddSeconds(1), Stopped));
        var pedalling = engine.Reduce(holding.State, new ClockTicked(Start.AddSeconds(2), Pedalling));
        var target = Single<ApplyPowerTarget>(pedalling);
        var applied = engine.Reduce(pedalling.State, new PowerTargetCompleted(Start.AddSeconds(3), target, true));

        using (Assert.Multiple())
        {
            await Assert.That(hold.Purpose).IsEqualTo(TrainingTargetPurpose.PedallingHold);
            await Assert.That(hold.Watts).IsEqualTo(Capabilities.MinimumWatts);
            await Assert.That(holding.State.IsWaitingForPedalling).IsTrue();
            await Assert.That(stillWaiting.State).IsSameReferenceAs(holding.State);
            await Assert.That(target.Purpose).IsEqualTo(TrainingTargetPurpose.SegmentTarget);
            await Assert.That(target.SegmentIndex).IsEqualTo(0);
            await Assert.That(applied.State.RunnerState).IsEqualTo(TrainingWorkoutState.Running);
            await Assert.That(applied.State.SegmentStartedAt).IsEqualTo(Start.AddSeconds(3));
            await Assert.That(applied.State.TargetPowerSamples).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task Tick_AdvancesToTheNextSegmentWhenTheIntervalEnds()
    {
        var running = StartRunning(CreateWorkout(), out var startedAt);

        var advanced = engine.Reduce(running, new ClockTicked(startedAt.AddMinutes(1), Pedalling));
        var next = Single<ApplyPowerTarget>(advanced);

        using (Assert.Multiple())
        {
            await Assert.That(advanced.State.Runner!.SegmentIndex).IsEqualTo(1);
            await Assert.That(advanced.State.RunnerState).IsEqualTo(TrainingWorkoutState.ApplyingTarget);
            await Assert.That(next.SegmentIndex).IsEqualTo(1);
            await Assert.That(next.Revision).IsEqualTo(advanced.State.Revision);
        }
    }

    [Test]
    public async Task FinalTick_CompletesTheWorkoutAndEndsTheMachineSession()
    {
        var running = StartRunning(CreateWorkout(segmentCount: 1), out var startedAt);

        var completed = engine.Reduce(running, new ClockTicked(startedAt.AddMinutes(1), Pedalling));
        var ended = engine.Reduce(completed.State, new MachineSessionEnded(startedAt.AddMinutes(1), Single<EndMachineSession>(completed), true));

        using (Assert.Multiple())
        {
            await Assert.That(completed.State.IsEnding).IsTrue();
            await Assert.That(Single<EndMachineSession>(completed).Purpose).IsEqualTo(TrainingEndPurpose.WorkoutComplete);
            await Assert.That(ended.State.IsActive).IsFalse();
            await Assert.That(ended.Effects).Contains(new NavigateTo(TrainingDestination.Results));
            await Assert.That(ended.Effects.OfType<RecordHeartRateVolume>()).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task StaleTargetResult_IsIgnoredAfterTheRevisionMovesOn()
    {
        var running = StartRunning(CreateWorkout(), out var startedAt);
        var stale = new ApplyPowerTarget(TrainingTargetPurpose.HeldTarget, running.Generation, running.Revision - 1, 0, 400);

        var transition = engine.Reduce(running, new PowerTargetCompleted(startedAt, stale, true));

        await Assert.That(transition.State.CurrentTargetWatts).IsEqualTo(running.CurrentTargetWatts);
    }

    [Test]
    public async Task LowCadence_HoldsMinimumPowerAndRecoveryRampsBack()
    {
        var running = StartRunning(CreateWorkout(), out var startedAt);
        var session = running;
        TrainingTransition stalled = TrainingTransition.Of(session);
        for (var second = 1; second <= 10 && !session.CadenceGuard.IsStalled; second++)
        {
            stalled = engine.Reduce(session, new ClockTicked(startedAt.AddSeconds(second), Stopped));
            session = stalled.State;
        }

        var held = Single<ApplyPowerTarget>(stalled);
        var recovered = engine.Reduce(session, new ClockTicked(startedAt.AddSeconds(12), Pedalling));

        using (Assert.Multiple())
        {
            await Assert.That(session.IsStalled).IsTrue();
            await Assert.That(session.StatusText).IsEqualTo(TrainingSessionText.Stalled);
            await Assert.That(held.Purpose).IsEqualTo(TrainingTargetPurpose.HeldTarget);
            await Assert.That(held.Watts).IsEqualTo(Capabilities.MinimumWatts);
            await Assert.That(recovered.State.IsStalled).IsFalse();
        }
    }

    [Test]
    public async Task HeartRateSignalLoss_AppliesTheSafeTargetThenPauses()
    {
        var workout = CreateHeartRateWorkout();
        var fresh = new TrainingTelemetry(90, 120, new HeartRateObservation(Start, 120), null);
        var running = StartRunning(workout, out var startedAt, fresh);

        var lost = engine.Reduce(running, new ClockTicked(startedAt.AddSeconds(30), Pedalling));
        var safe = Single<ApplyPowerTarget>(lost);
        var repeated = engine.Reduce(lost.State, new ClockTicked(startedAt.AddSeconds(31), Pedalling));
        var lowered = engine.Reduce(lost.State, new PowerTargetCompleted(startedAt.AddSeconds(31), safe, true));
        var pause = Single<PauseMachineSession>(lowered);
        var paused = engine.Reduce(lowered.State, new MachineSessionPaused(startedAt.AddSeconds(32), pause, true));

        using (Assert.Multiple())
        {
            await Assert.That(safe.Purpose).IsEqualTo(TrainingTargetPurpose.HeartRateSafeTarget);
            await Assert.That(safe.Watts).IsEqualTo(workout.Segments[0].MinimumTargetWatts);
            await Assert.That(repeated.Effects).IsEmpty();
            await Assert.That(pause.Purpose).IsEqualTo(TrainingPausePurpose.HeartRateSignalLoss);
            await Assert.That(pause.SafeTargetAccepted).IsTrue();
            await Assert.That(paused.State.RunnerState).IsEqualTo(TrainingWorkoutState.Paused);
            await Assert.That(paused.State.IsHeartRateSignalHandling).IsFalse();
        }
    }

    [Test]
    public async Task End_WhileTheStartIsInFlight_WaitsForTheStartResultBeforeStopping()
    {
        var begun = engine.Reduce(TrainingSession.Idle with { Machine = Running }, Begun(CreateWorkout()));
        var start = Single<StartMachineSession>(begun);

        var ending = engine.Reduce(begun.State, new EndRequested(Start));
        var started = engine.Reduce(ending.State, new MachineSessionStarted(Start, start, true, "Connected"));
        var ended = engine.Reduce(started.State, new MachineSessionEnded(Start, Single<EndMachineSession>(started), true));

        using (Assert.Multiple())
        {
            await Assert.That(ending.Effects.OfType<EndMachineSession>()).IsEmpty();
            await Assert.That(ending.State.IsEndAwaitingStart).IsTrue();
            await Assert.That(started.Effects.OfType<ApplyPowerTarget>()).IsEmpty();
            await Assert.That(ended.State.RunnerState).IsEqualTo(TrainingWorkoutState.Aborted);
            await Assert.That(ended.State.IsActive).IsFalse();
            await Assert.That(ended.Effects).Contains(new NavigateTo(TrainingDestination.Results));
            await Assert.That(ended.Effects).Contains(new ResetSafetyAcknowledgements());
        }
    }

    [Test]
    public async Task Disconnect_AbortsTheRunnerAndLeavesTheRideLocallyEndable()
    {
        var running = StartRunning(CreateWorkout(), out var startedAt);

        var detached = engine.Reduce(running, new MachineStateChanged(startedAt, new TrainingMachine(false, TrainingMachineState.NotStarted)));
        var ended = engine.Reduce(detached.State, new EndRequested(startedAt));

        using (Assert.Multiple())
        {
            await Assert.That(detached.State.RunnerState).IsEqualTo(TrainingWorkoutState.Aborted);
            await Assert.That(detached.State.IsLocallyEndable).IsTrue();
            await Assert.That(detached.State.IsActive).IsTrue();
            await Assert.That(detached.State.Generation).IsGreaterThan(running.Generation);
            await Assert.That(ended.Effects).Contains(new EndMachineSession(TrainingEndPurpose.Rider));
        }
    }

    [Test]
    public async Task RampTestFinish_EstimatesFtpAndReturnsToTraining()
    {
        var samples = Enumerable.Range(0, 120)
            .Select(static second => new RecordedTelemetrySample(Start.AddSeconds(second), 250, null, 90, null))
            .ToArray();
        var rampEngine = new TrainingSessionEngine(_ => samples);
        var begun = rampEngine.Reduce(
            TrainingSession.Idle with { Machine = Running },
            Begun(RampFtpProtocol.Create(Capabilities), isRampTest: true));
        var started = rampEngine.Reduce(begun.State, new MachineSessionStarted(Start, Single<StartMachineSession>(begun), true, "Connected"));

        var finishing = rampEngine.Reduce(started.State, new RampTestFinishRequested(Start.AddMinutes(2)));
        var finished = rampEngine.Reduce(finishing.State, new MachineSessionEnded(Start.AddMinutes(2), Single<EndMachineSession>(finishing), true));

        using (Assert.Multiple())
        {
            await Assert.That(started.State.CanFinishRampTest).IsTrue();
            await Assert.That(Single<EndMachineSession>(finishing).Purpose).IsEqualTo(TrainingEndPurpose.RampTestFinished);
            await Assert.That(finished.State.RampEstimate).IsNotNull();
            await Assert.That(finished.State.IsRampTest).IsFalse();
            await Assert.That(finished.State.IsActive).IsFalse();
            await Assert.That(finished.Effects).Contains(new NavigateTo(TrainingDestination.Training));
        }
    }

    private TrainingSession StartRunning(CompiledTrainingWorkout workout, out DateTimeOffset startedAt, TrainingTelemetry? telemetry = null)
    {
        var pedalling = telemetry ?? Pedalling;
        var begun = engine.Reduce(TrainingSession.Idle with { Machine = Running }, Begun(workout));
        var started = engine.Reduce(begun.State, new MachineSessionStarted(Start, Single<StartMachineSession>(begun), true, "Connected"));
        var holding = engine.Reduce(started.State, new PowerTargetCompleted(Start, Single<ApplyPowerTarget>(started), true));
        var pedallingTick = engine.Reduce(holding.State, new ClockTicked(Start, pedalling));
        var applied = engine.Reduce(pedallingTick.State, new PowerTargetCompleted(Start, Single<ApplyPowerTarget>(pedallingTick), true));
        startedAt = Start;
        return applied.State;
    }

    private static WorkoutBegun Begun(CompiledTrainingWorkout workout, bool isRampTest = false) =>
        new(Start, workout, Capabilities, isRampTest, TrainingWorkoutCompiler.ReferenceCadenceRpm, "200");

    private static T Single<T>(TrainingTransition transition)
        where T : TrainingSessionEffect =>
        transition.Effects.OfType<T>().Single();

    private static CompiledTrainingWorkout CreateWorkout(int segmentCount = 2)
    {
        var segments = Enumerable.Range(0, segmentCount)
            .Select(static index => new TrainingSegment($"Interval {index + 1}", TimeSpan.FromMinutes(1), 75d, 80, 100, "Ride steady", true))
            .ToArray();
        var definition = new TrainingWorkout("test", "Test", "Test workout", TrainingObjective.Endurance, TrainingExperienceLevel.Beginner, segments);
        return new CompiledTrainingWorkout(definition, [.. segments.Select(static segment => new CompiledTrainingSegment(segment, 150))]);
    }

    private static CompiledTrainingWorkout CreateHeartRateWorkout()
    {
        var segment = new TrainingSegment(
            "Zone 2",
            TimeSpan.FromMinutes(10),
            60d,
            null,
            null,
            "Hold zone 2",
            true,
            HeartRateTrainingTarget.ForRange(110, 130, 50d, 70d));
        var definition = new TrainingWorkout("hr", "Heart rate", "Heart-rate workout", TrainingObjective.HeartRate, TrainingExperienceLevel.Beginner, [segment])
        {
            Kind = TrainingWorkoutKind.HeartRateZone,
        };
        return new CompiledTrainingWorkout(definition, [new CompiledTrainingSegment(segment, 120, 100, 140, 110, 130)]);
    }
}