using System.Collections.Immutable;
using System.Globalization;

namespace Stationary.Ftms.Dashboard.Core;

// The reducer's output: the next state plus the side effects the host must run, in order.
public sealed record TrainingTransition(TrainingSession State, ImmutableList<TrainingSessionEffect> Effects)
{
    public static TrainingTransition Of(TrainingSession state) => new(state, []);

    public TrainingTransition Emit(TrainingSessionEffect effect) => this with { Effects = Effects.Add(effect) };

    public TrainingTransition Update(Func<TrainingSession, TrainingSession> update) => this with { State = update(State) };

    public TrainingTransition Then(Func<TrainingSession, TrainingTransition> step)
    {
        var next = step(State);
        return new(next.State, Effects.AddRange(next.Effects));
    }
}

// Pure training workflow: (state, event) -> (state, effects). Device I/O happens outside and returns as result events.
public sealed class TrainingSessionEngine(Func<DateTimeOffset, IReadOnlyList<RecordedTelemetrySample>> readSamplesSince)
{
    public static readonly TimeSpan HeartRateAdjustmentInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan HeartRateSettlingDuration = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaximumHeartRateSampleAge = TimeSpan.FromSeconds(5);

    public TrainingTransition Reduce(TrainingSession state, TrainingSessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(state);
        return sessionEvent switch
        {
            WorkoutBegun begun => Begin(state, begun),
            ClockTicked tick => Tick(state, tick),
            MachineStateChanged changed => OnMachineStateChanged(state with { Machine = changed.Machine }, changed.At),
            PauseRequested => RequestPause(state),
            ResumeRequested => state.CanResume
                ? TrainingTransition.Of(state with { IsResumePending = true }).Emit(new ResumeMachineSession())
                : TrainingTransition.Of(state),
            RetryRequested => Retry(state),
            EndRequested => RequestEnd(state),
            RampTestFinishRequested => FinalizeRampTest(state, allStepsExhausted: false),
            NoticePosted notice => TrainingTransition.Of(state with { StatusText = notice.Text }),
            PowerTargetCompleted completed => OnPowerTargetCompleted(state, completed),
            MachineSessionStarted started => OnMachineSessionStarted(state, started),
            MachineSessionPaused paused => OnMachineSessionPaused(state, paused),
            MachineSessionResumed resumed => OnMachineSessionResumed(state, resumed),
            MachineSessionEnded ended => OnMachineSessionEnded(state, ended),
            HeartRateVolumeRecorded recorded => TrainingTransition.Of(state with
            {
                HeartRateProgressRecorded = state.HeartRateProgressRecorded || recorded.Recorded,
                StatusText = string.IsNullOrEmpty(recorded.Text) ? recorded.Request.StatusPrefix : $"{recorded.Request.StatusPrefix} {recorded.Text}",
            }),
            OperationFailed failed => TrainingTransition.Of(state with { StatusText = failed.Message }),
            _ => throw new ArgumentOutOfRangeException(nameof(sessionEvent), sessionEvent, "Unknown training event."),
        };
    }

    public static bool HasFreshHeartRate(TrainingTelemetry telemetry, DateTimeOffset now) =>
        telemetry.HeartRate is { } observation
        && now >= observation.CapturedAt
        && now - observation.CapturedAt <= MaximumHeartRateSampleAge;

    private static TrainingTransition Begin(TrainingSession state, WorkoutBegun begun)
    {
        var workout = begun.Workout;
        var generation = state.Generation + 1;
        return TrainingTransition.Of(TrainingSession.Idle with
        {
            Runner = new TrainingWorkoutRunner(workout).Start(),
            Capabilities = begun.Capabilities,
            IsRampTest = begun.IsRampTest,
            RampStartedAt = begun.IsRampTest ? begun.At : null,
            RampEstimate = begun.IsRampTest ? null : state.RampEstimate,
            PreferredCadenceRpm = begun.PreferredCadenceRpm,
            WorkoutEventText = TrainingEventLabels.Workout(workout.Definition.Name, begun.FunctionalThresholdPowerText, begun.PreferredCadenceRpm),
            WorkoutStartedAt = begun.At,
            Machine = state.Machine,
            Generation = generation,
            Revision = state.Revision + 1,
            IsSessionStartPending = true,
            IsSessionStartInFlight = true,
            RequiresPedallingBeforeTarget = true,
            LastHeartRateAdjustmentAt = begun.At,
            AdherenceText = workout.Segments.Any(static segment => segment.TargetMinimumHeartRate is not null)
                ? TrainingSessionText.HeartRateAdherencePending
                : TrainingSessionText.CadenceAdherencePending,
            StatusText = "Requesting control and starting the bike session...",
        })
            .Emit(new NavigateTo(TrainingDestination.Ride))
            .Emit(new StartMachineSession(generation, AnnounceWorkout: true));
    }

    private static TrainingTransition OnMachineSessionStarted(TrainingSession state, MachineSessionStarted started)
    {
        state = state with { IsSessionStartInFlight = false, HasSessionStarted = state.HasSessionStarted || started.Accepted };
        if (state.IsEndAwaitingStart)
        {
            return TrainingTransition.Of(state with
            {
                IsEndAwaitingStart = false,
                EndHadStarted = state.EndHadStarted || started.Accepted || state.Machine.IsSessionOpen,
            }).Emit(new EndMachineSession(TrainingEndPurpose.Rider));
        }

        if (started.Request.Generation != state.Generation)
        {
            return TrainingTransition.Of(state);
        }

        if (!started.Accepted)
        {
            return TrainingTransition.Of(state with
            {
                Runner = state.Runner?.RejectTarget(),
                StatusText = $"The bike did not confirm the session start. Retry or end. {started.ConnectionStatus}",
            });
        }

        var transition = TrainingTransition.Of(state with { IsSessionStartPending = false });
        return (started.Request.AnnounceWorkout ? transition.Emit(new AddTrainingEvent(state.WorkoutEventText)) : transition)
            .Then(StartSegmentTarget);
    }

    private static TrainingTransition Retry(TrainingSession state)
    {
        if (!state.CanRetry)
        {
            return TrainingTransition.Of(state);
        }

        state = state with { Runner = state.Runner?.RetryTarget() };
        return state.IsSessionStartPending
            ? TrainingTransition.Of(state with
            {
                IsSessionStartInFlight = true,
                StatusText = "Requesting control and starting the bike session...",
            }).Emit(new StartMachineSession(state.Generation, AnnounceWorkout: false))
            : StartSegmentTarget(state);
    }

    private static TrainingTransition StartSegmentTarget(TrainingSession state) =>
        state.RequiresPedallingBeforeTarget ? HoldUntilPedalling(state) : ApplyCurrentTarget(state);

    // Holding the lowest target until the rider pedals prevents the trainer from loading resistance against a stopped flywheel.
    private static TrainingTransition HoldUntilPedalling(TrainingSession state) => state.Runner is { } runner
        ? TrainingTransition.Of(state with { StatusText = "Preparing the bike..." })
            .Emit(new ApplyPowerTarget(TrainingTargetPurpose.PedallingHold, state.Generation, state.Revision, runner.SegmentIndex, state.Capabilities.MinimumWatts))
        : TrainingTransition.Of(state);

    private static TrainingTransition ApplyCurrentTarget(TrainingSession state)
    {
        if (state.Runner is not { CurrentSegment: { } segment } runner)
        {
            return TrainingTransition.Of(state);
        }

        var capabilities = state.Capabilities;
        var isHeldLow = state.CadenceGuard.IsStalled && !state.IsRampTest;
        var rampFrom = segment.Definition.HeartRateTarget is not null && state.LastAppliedTargetWatts is int previous
            ? int.Clamp(previous, segment.MinimumTargetWatts, segment.MaximumTargetWatts)
            : state.LastAppliedTargetWatts;
        var ramp = !state.IsRampTest && !isHeldLow
            ? TrainingTargetRamp.Create(rampFrom, segment, capabilities)
            : new TrainingTargetRamp(segment.TargetWatts, segment.TargetWatts, TimeSpan.Zero);
        var initialWatts = state.IsRampTest
            ? segment.TargetWatts
            : isHeldLow ? capabilities.MinimumWatts : ramp.GetTargetWatts(TimeSpan.Zero, capabilities);
        var revision = state.Revision + 1;
        return TrainingTransition.Of(state with
        {
            Revision = revision,
            CurrentTargetWatts = initialWatts,
            StatusText = ramp.Duration > TimeSpan.Zero
                ? $"Ramping toward {segment.TargetWatts} W for {segment.Definition.Name}."
                : $"Applying {segment.TargetWatts} W for {segment.Definition.Name}.",
        })
            .Emit(new AddTrainingEvent(TrainingEventLabels.Interval(runner.SegmentIndex, runner.SegmentCount, segment)))
            .Emit(new ApplyPowerTarget(TrainingTargetPurpose.SegmentTarget, state.Generation, revision, runner.SegmentIndex, initialWatts, ramp));
    }

    private static TrainingTransition RequestPause(TrainingSession state) => state.CanPause
        ? TrainingTransition.Of(state with { IsPausePending = true })
            .Emit(new PauseMachineSession(TrainingPausePurpose.Rider, state.Generation))
        : TrainingTransition.Of(state);

    private static TrainingTransition OnMachineSessionPaused(TrainingSession state, MachineSessionPaused paused)
    {
        var request = paused.Request;
        if (request.Purpose == TrainingPausePurpose.Rider)
        {
            return TrainingTransition.Of(paused.Accepted
                ? state with { IsPausePending = false, Runner = state.Runner?.Pause(paused.At), StatusText = "Training timer paused." }
                : state with { IsPausePending = false, StatusText = "The bike session could not be paused." });
        }

        if (request.Generation != state.Generation)
        {
            return TrainingTransition.Of(state with { IsHeartRateSignalHandling = false });
        }

        return TrainingTransition.Of(state with
        {
            IsHeartRateSignalHandling = false,
            Runner = state.Runner?.Pause(paused.At),
            StatusText = !request.SafeTargetAccepted
                ? $"Heart-rate signal lost and the bike rejected the safe power target ({state.Capabilities.MinimumWatts} W). End the workout."
                : paused.Accepted
                    ? "Heart-rate signal lost. Power reduced and workout paused. Reconnect the sensor before resuming."
                    : "Heart-rate signal lost. Power was lowered where possible; end the workout if the sensor cannot be restored.",
        });
    }

    private static TrainingTransition OnMachineSessionResumed(TrainingSession state, MachineSessionResumed resumed) =>
        TrainingTransition.Of(resumed.Accepted
            ? state with { IsResumePending = false, Runner = state.Runner?.Resume(resumed.At), StatusText = "Training timer resumed." }
            : state with { IsResumePending = false, StatusText = "The bike session could not be resumed." });

    private static TrainingTransition RequestEnd(TrainingSession state)
    {
        if (state.IsEnding || !state.IsActive)
        {
            return TrainingTransition.Of(state);
        }

        var awaitStart = state.IsSessionStartPending && state.Machine.IsConnected && state.IsSessionStartInFlight;
        var transition = TrainingTransition.Of(state with
        {
            IsEnding = true,
            IsEndFailed = false,
            IsLocallyEndable = false,
            Generation = state.Generation + 1,
            IsEndAwaitingStart = awaitStart,
            EndHadStarted = state.HasSessionStarted,
            StatusText = "Ending training and waiting for the bike...",
        }).Emit(new AddTrainingEvent(state.Runner?.CurrentSegment is { } segment
            ? $"Workout ended by rider during {segment.Definition.Name}"
            : "Workout ended by rider"));
        return awaitStart ? transition : transition.Emit(new EndMachineSession(TrainingEndPurpose.Rider));
    }

    private static TrainingTransition CompleteWorkout(TrainingSession state) =>
        TrainingTransition.Of(state with
        {
            IsEnding = true,
            IsEndFailed = false,
            StatusText = "Workout complete. Waiting for the bike to stop...",
        })
            .Emit(new AddTrainingEvent("Workout complete"))
            .Emit(new EndMachineSession(TrainingEndPurpose.WorkoutComplete));

    private static TrainingTransition FinalizeRampTest(TrainingSession state, bool allStepsExhausted) =>
        state.IsRampTest && state.RampStartedAt is not null && !state.IsEnding
            ? TrainingTransition.Of(state with
            {
                IsEnding = true,
                IsEndFailed = false,
                IsLocallyEndable = false,
                Generation = state.Generation + 1,
                StatusText = "Ending the ramp test and waiting for the bike...",
            }).Emit(new EndMachineSession(allStepsExhausted ? TrainingEndPurpose.RampTestExhausted : TrainingEndPurpose.RampTestFinished))
            : TrainingTransition.Of(state);

    private TrainingTransition OnMachineSessionEnded(TrainingSession state, MachineSessionEnded ended)
    {
        var purpose = ended.Request.Purpose;
        if (!ended.Succeeded)
        {
            return TrainingTransition.Of(state with
            {
                IsEnding = false,
                IsEndFailed = true,
                StatusText = purpose switch
                {
                    TrainingEndPurpose.Rider => "The bike did not confirm the end. Retry ending training before starting another ride.",
                    TrainingEndPurpose.WorkoutComplete => "The final interval completed, but the bike did not confirm the session end. Retry ending training.",
                    _ => "The bike did not confirm the ramp-test end. Retry ending training.",
                },
            });
        }

        var closed = state with
        {
            IsEnding = false,
            IsEndFailed = false,
            IsLocallyEndable = false,
            HasSessionStarted = false,
        };
        return purpose switch
        {
            TrainingEndPurpose.Rider => RecordVolume(
                TrainingTransition.Of(closed with
                {
                    IsSessionStartPending = false,
                    Runner = closed.Runner?.Abort(),
                    IsWaitingForPedalling = false,
                    IsRampTest = false,
                }).Emit(new ResetSafetyAcknowledgements()),
                "Training ended.")
                .Emit(new NavigateTo(state.EndHadStarted ? TrainingDestination.Results : TrainingDestination.Today)),
            TrainingEndPurpose.WorkoutComplete => RecordVolume(
                TrainingTransition.Of(closed).Emit(new ResetSafetyAcknowledgements()),
                "Workout complete.")
                .Emit(new NavigateTo(TrainingDestination.Results)),
            _ => FinishRampTest(closed, purpose == TrainingEndPurpose.RampTestExhausted),
        };
    }

    private TrainingTransition FinishRampTest(TrainingSession state, bool allStepsExhausted)
    {
        var estimate = allStepsExhausted || state.RampStartedAt is not { } startedAt
            ? new RampFtpEstimateDeviceMaximumReached()
            : RampFtpEstimator.Estimate(readSamplesSince(startedAt), state.Capabilities.MaximumWatts);
        return TrainingTransition.Of(state with
        {
            Runner = state.Runner?.Abort(),
            IsWaitingForPedalling = false,
            RampEstimate = estimate,
            IsRampTest = false,
            StatusText = TrainingSessionText.Describe(estimate),
        }).Emit(new NavigateTo(TrainingDestination.Training));
    }

    private static TrainingTransition RecordVolume(TrainingTransition transition, string statusPrefix) =>
        !transition.State.HeartRateProgressRecorded && transition.State.WorkoutStartedAt is { } startedAt
            ? transition.Update(state => state with { StatusText = statusPrefix }).Emit(new RecordHeartRateVolume(startedAt, statusPrefix))
            : transition.Update(state => state with { StatusText = statusPrefix });

    private static TrainingTransition OnMachineStateChanged(TrainingSession state, DateTimeOffset at)
    {
        if (state.Runner is not { } runner || !state.IsActive || state.IsEnding)
        {
            return TrainingTransition.Of(state);
        }

        return state.Machine switch
        {
            { IsConnected: false } => TrainingTransition.Of(Detach(state, "Connection lost. End training locally to leave the ride.")),
            { State: TrainingMachineState.Paused } when runner.State == TrainingWorkoutState.Running =>
                TrainingTransition.Of(state with { Runner = runner.Pause(at), StatusText = "The bike paused. Training timer paused." }),
            { State: TrainingMachineState.Running } when runner.State == TrainingWorkoutState.Paused =>
                TrainingTransition.Of(state with { Runner = runner.Resume(at), StatusText = "The bike resumed. Training timer resumed." }),
            { State: TrainingMachineState.Stopped } when runner.State is not (TrainingWorkoutState.Completed or TrainingWorkoutState.Aborted) =>
                TrainingTransition.Of(Detach(state, "The bike stopped the session. End training locally to leave the ride.")),
            _ => TrainingTransition.Of(state),
        };
    }

    private static TrainingSession Detach(TrainingSession state, string status) => state with
    {
        Generation = state.Generation + 1,
        IsSessionStartPending = false,
        Runner = state.Runner?.Abort(),
        IsRampTest = false,
        IsEndFailed = false,
        IsLocallyEndable = true,
        StatusText = status,
    };

    private TrainingTransition Tick(TrainingSession state, ClockTicked tick)
    {
        if (state.Runner is not { } runner
            || state.IsPausePending
            || state.IsEnding
            || state.IsEndFailed
            || runner.State == TrainingWorkoutState.Paused
            || state.Machine.State != TrainingMachineState.Running)
        {
            return TrainingTransition.Of(state);
        }

        if (runner.CurrentSegment is { Definition.HeartRateTarget: not null } && !HasFreshHeartRate(tick.Telemetry, tick.At))
        {
            return HandleHeartRateSignalLoss(state);
        }

        return AdjustHeartRate(state, tick).Then(next => AdvanceWorkout(next, tick));
    }

    private static TrainingTransition HandleHeartRateSignalLoss(TrainingSession state) =>
        state.IsHeartRateSignalHandling || state.IsRampTest || state.Runner is not { CurrentSegment: { } segment } runner
            ? TrainingTransition.Of(state)
            : TrainingTransition.Of(state with { IsHeartRateSignalHandling = true })
                .Emit(new ApplyPowerTarget(TrainingTargetPurpose.HeartRateSafeTarget, state.Generation, state.Revision, runner.SegmentIndex, segment.MinimumTargetWatts));

    private static bool ShouldAdjustHeartRate(TrainingSession state, ClockTicked tick)
    {
        if (state.IsHeartRateAdjustmentPending
            || state.Runner is not { State: TrainingWorkoutState.Running, CurrentSegment: { } segment }
            || !HasFreshHeartRate(tick.Telemetry, tick.At)
            || tick.At - state.LastHeartRateAdjustmentAt < HeartRateAdjustmentInterval)
        {
            return false;
        }

        var target = segment.Definition.HeartRateTarget;
        if (target is not null
            && state.SegmentStartedAt is { } startedAt
            && tick.At - startedAt < HeartRateSettlingDuration)
        {
            return tick.Telemetry.SafetyCeilingHeartRate is ushort ceiling
                && tick.Telemetry.HeartRate is { } latest
                && latest.BeatsPerMinute >= ceiling;
        }

        return target is not null || tick.Telemetry.SafetyCeilingHeartRate is not null;
    }

    private static TrainingTransition AdjustHeartRate(TrainingSession state, ClockTicked tick)
    {
        if (!ShouldAdjustHeartRate(state, tick)
            || state.Runner is not { CurrentSegment: { } segment } runner
            || tick.Telemetry.HeartRate is not { } observation)
        {
            return TrainingTransition.Of(state);
        }

        var capabilities = state.Capabilities;
        var ceiling = tick.Telemetry.SafetyCeilingHeartRate;
        var isHeartRateSegment = segment.Definition.HeartRateTarget is not null;
        var current = state.CurrentTargetWatts;
        var nextWatts = HeartRatePowerController.GetNextTargetWatts(
            current,
            isHeartRateSegment ? segment.MinimumTargetWatts : capabilities.MinimumWatts,
            isHeartRateSegment ? segment.MaximumTargetWatts : capabilities.MaximumWatts,
            capabilities.IncrementWatts,
            observation.BeatsPerMinute,
            segment.TargetMinimumHeartRate ?? 0,
            segment.TargetMaximumHeartRate ?? ushort.MaxValue,
            ceiling);
        state = state with { LastHeartRateAdjustmentAt = tick.At };
        var bpm = observation.BeatsPerMinute;
        if (nextWatts == current)
        {
            var notice = (ceiling, segment.TargetMinimumHeartRate, segment.TargetMaximumHeartRate) switch
            {
                (ushort limit, _, _) when bpm >= limit =>
                    "Heart rate remains at or above the configured ceiling at minimum power. Stop the workout if it does not come down or you feel unwell.",
                (_, ushort lower, _) when bpm < lower && current >= segment.MaximumTargetWatts =>
                    "Heart rate remains below target at the configured power cap; the app will not exceed that limit.",
                (_, _, ushort upper) when bpm > upper && current <= segment.MinimumTargetWatts =>
                    "Heart rate remains above target at minimum power. Stop if you feel unwell.",
                _ => null,
            };
            return TrainingTransition.Of(notice is null ? state : state with { StatusText = notice });
        }

        var acceptedNotice = ceiling is ushort reached && bpm >= reached
            ? $"Heart rate reached the configured ceiling ({ceiling} bpm); power reduced to {nextWatts} W."
            : segment.TargetMinimumHeartRate is ushort minimum && segment.TargetMaximumHeartRate is ushort maximum
                ? $"Adjusting power to hold {minimum}-{maximum} bpm."
                : $"Heart rate reached the configured ceiling ({ceiling} bpm); power reduced to {nextWatts} W.";
        return TrainingTransition.Of(state with { IsHeartRateAdjustmentPending = true })
            .Emit(new ApplyPowerTarget(TrainingTargetPurpose.HeartRateAdjustment, state.Generation, state.Revision, runner.SegmentIndex, nextWatts, AcceptedNotice: acceptedNotice));
    }

    private TrainingTransition AdvanceWorkout(TrainingSession state, ClockTicked tick)
    {
        var cadence = tick.Telemetry.CadenceRpm;
        if (state.IsWaitingForPedalling)
        {
            return TrainingCadenceGuard.IsPedalling(cadence)
                ? TrainingTransition.Of(state with { IsWaitingForPedalling = false, RequiresPedallingBeforeTarget = false })
                    .Emit(new AddTrainingEvent(TrainingEventLabels.Pedalling(cadence)))
                    .Then(ApplyCurrentTarget)
                : TrainingTransition.Of(state);
        }

        if (state.Runner is not { } runner)
        {
            return TrainingTransition.Of(state);
        }

        var advanced = runner.Advance(tick.At);
        if (ReferenceEquals(advanced, runner))
        {
            return GuardCadence(state, tick)
                .Then(next => ObserveResistanceFloor(next, tick))
                .Then(next => ContinueTargetRamp(next, tick.At));
        }

        var completedSegment = runner.CurrentSegment;
        var completedIndex = runner.SegmentIndex;
        return TrainingTransition.Of(RecordAdherence(state with { Runner = advanced }, completedSegment, completedIndex, tick.At))
            .Then(next => CalibrateCadence(next, completedIndex))
            .Then(next => advanced.State != TrainingWorkoutState.Completed
                ? ApplyCurrentTarget(next)
                : next.IsRampTest ? FinalizeRampTest(next, allStepsExhausted: true) : CompleteWorkout(next));
    }

    private static TrainingTransition GuardCadence(TrainingSession state, ClockTicked tick)
    {
        if (state.IsRampTest
            || state.Runner is not { State: TrainingWorkoutState.Running, CurrentSegment: { } segment } runner
            || state.GetSegmentElapsed(tick.At) is not { } elapsed)
        {
            return TrainingTransition.Of(state);
        }

        var capabilities = state.Capabilities;
        var cadence = tick.Telemetry.CadenceRpm;
        var (guard, transition) = state.CadenceGuard.Update(tick.At, cadence);
        state = state with { CadenceGuard = guard };
        switch (transition)
        {
            case TrainingCadenceGuardTransition.Stalled:
                var stalledRevision = state.Revision + 1;
                return TrainingTransition.Of(state with { Revision = stalledRevision, ActiveRamp = default, StatusText = TrainingSessionText.Stalled })
                    .Emit(new AddTrainingEvent(TrainingEventLabels.Stalled(cadence, capabilities.MinimumWatts)))
                    .Emit(new ApplyPowerTarget(TrainingTargetPurpose.HeldTarget, state.Generation, stalledRevision, runner.SegmentIndex, capabilities.MinimumWatts));
            case TrainingCadenceGuardTransition.Recovered:
                // Start from what the rider already produces, and never below the bike's floor at this cadence:
                // targets under the floor left the restart spike in place, while targets at or above it ended it.
                var rampFrom = int.Max(
                    int.Max(state.LastAppliedTargetWatts ?? capabilities.MinimumWatts, tick.Telemetry.PowerWatts is double livePower ? (int)livePower : 0),
                    cadence is double recoveredCadence ? state.ResistanceFloor.EstimateMinimumWatts(recoveredCadence) ?? 0 : 0);
                if (segment.Definition.HeartRateTarget is not null)
                {
                    rampFrom = int.Clamp(rampFrom, segment.MinimumTargetWatts, segment.MaximumTargetWatts);
                }

                var ramp = TrainingTargetRamp.Create(rampFrom, segment, capabilities);
                var recoveredRevision = state.Revision + 1;
                var recovered = TrainingTransition.Of(state with
                {
                    Revision = recoveredRevision,
                    ActiveRamp = ramp,
                    ActiveRampStartElapsed = elapsed,
                    ActiveRampSegmentIndex = runner.SegmentIndex,
                    StatusText = $"Cadence recovered. Ramping back to {segment.TargetWatts} W.",
                }).Emit(new AddTrainingEvent(TrainingEventLabels.Recovered(cadence, segment.TargetWatts)));
                return ramp.Duration <= TimeSpan.Zero
                    ? recovered.Emit(new ApplyPowerTarget(TrainingTargetPurpose.HeldTarget, state.Generation, recoveredRevision, runner.SegmentIndex, segment.TargetWatts))
                    : recovered;
            default:
                return TrainingTransition.Of(state);
        }
    }

    private static bool IsTargetSettled(TrainingSession state, DateTimeOffset now) =>
        state.Runner is { State: TrainingWorkoutState.Running } runner
        && !state.CadenceGuard.IsStalled
        && state.GetSegmentElapsed(now) is { } elapsed
        && elapsed >= state.ActiveRampEndElapsed + state.GetSettlingDuration(runner.SegmentIndex);

    private static TrainingTransition ObserveResistanceFloor(TrainingSession state, ClockTicked tick)
    {
        if (state.IsRampTest || state.Runner is not { CurrentSegment: { } segment } runner)
        {
            return TrainingTransition.Of(state);
        }

        var cadence = tick.Telemetry.CadenceRpm;
        var settled = IsTargetSettled(state, tick.At);
        var (floor, changed) = state.ResistanceFloor.Observe(tick.At, tick.Telemetry.PowerWatts, cadence, segment.TargetWatts, settled);
        var transition = TrainingTransition.Of(state with { ResistanceFloor = floor });
        if (changed && floor.WattsPerRpm is double wattsPerRpm && cadence is double floorCadence)
        {
            transition = transition.Emit(new AddTrainingEvent(TrainingEventLabels.ResistanceFloor(wattsPerRpm, floorCadence)));
        }

        return settled
            && cadence is double liveCadence
            && state.ResistanceFloorNoticeSegmentIndex != runner.SegmentIndex
            && floor.IsAboveTarget(segment.TargetWatts, liveCadence)
            && floor.EstimateMinimumWatts(liveCadence) is int minimumWatts
                ? transition
                    .Update(next => next with { ResistanceFloorNoticeSegmentIndex = runner.SegmentIndex })
                    .Emit(new AddTrainingEvent(TrainingEventLabels.TargetBelowFloor(segment.TargetWatts, minimumWatts, liveCadence)))
                : transition;
    }

    private static TrainingTransition ContinueTargetRamp(TrainingSession state, DateTimeOffset now)
    {
        if (state.IsRampStepPending
            || state.CadenceGuard.IsStalled
            || state.IsRampTest
            || state.Runner is not { State: TrainingWorkoutState.Running } runner
            || runner.SegmentIndex != state.ActiveRampSegmentIndex
            || state.ActiveRamp.Duration <= TimeSpan.Zero
            || state.GetSegmentElapsed(now) is not { } elapsed)
        {
            return TrainingTransition.Of(state);
        }

        var watts = state.ActiveRamp.GetTargetWatts(elapsed - state.ActiveRampStartElapsed, state.Capabilities);
        return watts != state.LastAppliedTargetWatts
            ? TrainingTransition.Of(state with { IsRampStepPending = true })
                .Emit(new ApplyPowerTarget(TrainingTargetPurpose.RampStep, state.Generation, state.Revision, runner.SegmentIndex, watts))
            : TrainingTransition.Of(state);
    }

    // The warm-up shows the rider's natural cadence on this bike; later cadence ranges are rescaled to it within a bounded adjustment.
    private TrainingTransition CalibrateCadence(TrainingSession state, int completedIndex)
    {
        if (state.IsRampTest
            || state.Runner is not { } runner
            || state.SegmentStartedAt is not { } startedAt
            || !TrainingCadenceCalibration.IsCalibrationSegment(runner.Workout, completedIndex))
        {
            return TrainingTransition.Of(state);
        }

        var preferred = state.PreferredCadenceRpm;
        var samples = readSamplesSince(startedAt + state.SegmentRampDuration + state.GetSettlingDuration(completedIndex))
            .Where(static sample => sample.CadenceRpm is not null)
            .Select(static sample => sample.CadenceRpm!.Value);
        if (TrainingCadenceCalibration.MeasureMedianRpm(samples) is not { } measured)
        {
            return Calibrated(state, $"Not enough warm-up cadence to tune targets; using {preferred} rpm.");
        }

        var calibrated = TrainingCadenceCalibration.Limit(measured, preferred);
        if (calibrated == preferred)
        {
            return Calibrated(state, measured == calibrated
                ? $"Warm-up cadence matches {calibrated} rpm; targets unchanged."
                : $"Warm-up cadence {measured} rpm; targets stay at {calibrated} rpm.");
        }

        var rescaled = TrainingWorkoutCompiler.RescaleCadence(runner.Workout, calibrated, completedIndex + 1);
        return Calibrated(
            state with { Runner = runner.ReplaceWorkout(rescaled) },
            measured == calibrated
                ? $"Cadence targets tuned to your {calibrated} rpm warm-up."
                : $"Warm-up cadence {measured} rpm; targets tuned to {calibrated} rpm, the {TrainingCadenceCalibration.MaximumAdjustmentRpm} rpm limit per ride. Set Preferred cadence in Settings to match.");

        static TrainingTransition Calibrated(TrainingSession state, string text) =>
            TrainingTransition.Of(state with { CadenceCalibrationText = text }).Emit(new AddTrainingEvent(text));
    }

    private TrainingSession RecordAdherence(TrainingSession state, CompiledTrainingSegment? segment, int segmentIndex, DateTimeOffset completedAt)
    {
        if (segment is not { Definition.CountsTowardAdherence: true } || state.SegmentStartedAt is not { } startedAt)
        {
            return state;
        }

        if (segment.TargetMinimumHeartRate is ushort minimumHeartRate
            && segment.TargetMaximumHeartRate is ushort maximumHeartRate)
        {
            var heartRateResult = HeartRateTargetAdherenceCalculator.Calculate(
                readSamplesSince(startedAt),
                minimumHeartRate,
                maximumHeartRate,
                startedAt,
                completedAt,
                MaximumHeartRateSampleAge);
            return state with
            {
                AdherenceText = heartRateResult.Percentage is double percentage
                    ? string.Create(CultureInfo.InvariantCulture, $"Heart rate in target: {percentage:F0}% · {heartRateResult.InRange:hh\\:mm\\:ss} in range")
                    : "Heart-rate score unavailable: no fresh heart-rate samples were recorded.",
            };
        }

        var samples = readSamplesSince(startedAt)
            .Select(static sample => new TrainingAdherenceSample(sample.CapturedAt, sample.CadenceRpm))
            .ToArray();
        var result = TrainingAdherence.Calculate(
            samples,
            segment.Definition.MinimumCadenceRpm,
            segment.Definition.MaximumCadenceRpm,
            startedAt + state.SegmentRampDuration,
            completedAt,
            completed: true,
            state.GetSettlingDuration(segmentIndex));
        return state with
        {
            AdherenceText = result switch
            {
                TrainingAdherenceScore score => string.Create(CultureInfo.InvariantCulture, $"Cadence in range: {score.Percentage:F0}% · {score.Stars} star{(score.Stars == 1 ? string.Empty : "s")}"),
                TrainingAdherenceNoCadenceTarget => "Cadence score unavailable: this interval has no cadence target.",
                TrainingAdherenceNoEligibleCadence => "Cadence score unavailable: no cadence was measured after the trainer settling period.",
                _ => state.AdherenceText,
            },
        };
    }

    private static TrainingTransition OnPowerTargetCompleted(TrainingSession state, PowerTargetCompleted completed)
    {
        var request = completed.Request;
        var at = completed.At;
        var isCurrentSession = request.Generation == state.Generation;
        var isCurrentTarget = isCurrentSession && request.Revision == state.Revision;
        switch (request.Purpose)
        {
            case TrainingTargetPurpose.SegmentTarget when !isCurrentSession || state.Runner is not { } runner:
                return TrainingTransition.Of(state);
            case TrainingTargetPurpose.SegmentTarget when completed.Accepted:
                var confirmed = state.Runner.ConfirmTargetApplied(at);
                confirmed = state.Machine.State == TrainingMachineState.Paused ? confirmed.Pause(at) : confirmed;
                var segment = confirmed.Workout.Segments[request.SegmentIndex];
                var applied = state with
                {
                    Runner = confirmed,
                    SegmentStartedAt = at,
                    LastHeartRateAdjustmentAt = at,
                    SegmentRampDuration = request.Ramp.Duration,
                    TargetPowerSamples = state.TargetPowerSamples.Add(new TelemetrySample(at, request.Watts)),
                    StatusText = segment.Definition.Cue,
                };
                return TrainingTransition.Of(isCurrentTarget
                    ? applied with
                    {
                        ActiveRamp = request.Ramp,
                        ActiveRampStartElapsed = TimeSpan.Zero,
                        ActiveRampSegmentIndex = request.SegmentIndex,
                        LastAppliedTargetWatts = request.Watts,
                    }
                    : applied);
            case TrainingTargetPurpose.SegmentTarget:
                return TrainingTransition.Of(state with
                {
                    Runner = state.Runner.RejectTarget(),
                    StatusText = "The bike did not accept this target. Retry or end training.",
                });
            case TrainingTargetPurpose.PedallingHold when !isCurrentSession:
                return TrainingTransition.Of(state);
            case TrainingTargetPurpose.PedallingHold when completed.Accepted:
                return TrainingTransition.Of(state with
                {
                    CurrentTargetWatts = request.Watts,
                    IsWaitingForPedalling = true,
                    StatusText = TrainingSessionText.WaitingForPedalling,
                }).Emit(new AddTrainingEvent(string.Create(CultureInfo.InvariantCulture, $"Waiting for pedalling · holding {request.Watts} W")));
            case TrainingTargetPurpose.PedallingHold:
                return TrainingTransition.Of(state with
                {
                    Runner = state.Runner?.RejectTarget(),
                    StatusText = "The bike did not accept the starting target. Retry or end training.",
                });
            case TrainingTargetPurpose.HeldTarget:
                return TrainingTransition.Of(completed.Accepted && isCurrentTarget ? Applied(state, request.Watts, at) : state);
            case TrainingTargetPurpose.RampStep:
                state = state with { IsRampStepPending = false };
                return TrainingTransition.Of(completed.Accepted && isCurrentTarget ? Applied(state, request.Watts, at) : state);
            case TrainingTargetPurpose.HeartRateAdjustment:
                state = state with { IsHeartRateAdjustmentPending = false };
                if (!completed.Accepted || !isCurrentTarget || state.Runner?.SegmentIndex != request.SegmentIndex)
                {
                    return TrainingTransition.Of(state with { StatusText = "The bike rejected the heart-rate adjustment. The previous power target remains active." });
                }

                var notice = request.AcceptedNotice ?? string.Empty;
                return TrainingTransition.Of(Applied(state, request.Watts, at) with { StatusText = notice })
                    .Emit(new AddTrainingEvent(notice));
            case TrainingTargetPurpose.HeartRateSafeTarget:
                if (!isCurrentTarget || state.Runner?.SegmentIndex != request.SegmentIndex)
                {
                    return TrainingTransition.Of(state with { IsHeartRateSignalHandling = false });
                }

                var pause = new PauseMachineSession(TrainingPausePurpose.HeartRateSignalLoss, state.Generation, completed.Accepted);
                return completed.Accepted
                    ? TrainingTransition.Of(state with { CurrentTargetWatts = request.Watts, LastAppliedTargetWatts = request.Watts })
                        .Emit(new AddTrainingEvent($"Heart-rate signal lost; power reduced to {request.Watts} W."))
                        .Emit(pause)
                    : TrainingTransition.Of(state).Emit(pause);
            default:
                return TrainingTransition.Of(state);
        }
    }

    private static TrainingSession Applied(TrainingSession state, int watts, DateTimeOffset at) => state with
    {
        LastAppliedTargetWatts = watts,
        CurrentTargetWatts = watts,
        TargetPowerSamples = state.TargetPowerSamples.Add(new TelemetrySample(at, watts)),
    };
}