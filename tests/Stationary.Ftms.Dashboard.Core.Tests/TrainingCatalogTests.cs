using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingCatalogTests
{
    [Test]
    [Arguments(50, 400, 10, 104, 100)]
    [Arguments(50, 400, 10, 106, 110)]
    [Arguments(50, 400, 10, 105, 110)]
    [Arguments(55, 405, 10, 104, 105)]
    [Arguments(50, 109, 10, 109, 100)]
    [Arguments(50, 55, 10, 55, 50)]
    [Arguments(50, 400, 10, 50, 50)]
    [Arguments(50, 400, 10, 400, 400)]
    [Arguments(0, 500, 1, 104, 104)]
    public async Task Compile_UsesNearestSupportedPower(int minimum, int maximum, int increment, int requested, int expected)
    {
        var workout = new TrainingWorkout("test", "Test", "Test workout", TrainingObjective.Endurance,
            TrainingExperienceLevel.Beginner,
            [new("Work", TimeSpan.FromMinutes(1), 100d, null, null, "Ride", true)]);

        var compiled = TrainingWorkoutCompiler.Compile(workout, (ushort)requested, new(minimum, maximum, increment));

        await Assert.That(compiled.Segments[0].TargetWatts).IsEqualTo(expected);
    }

    [Test]
    public async Task Compile_AllCatalogWorkoutsSupportCoarsePowerIncrements()
    {
        var catalog = await TrainingCatalog.LoadAsync();

        foreach (var workout in catalog.Workouts)
        {
            var compiled = TrainingWorkoutCompiler.Compile(workout, 200, new(50, 500, 10));

            await Assert.That(compiled.Segments.All(static segment =>
                segment.TargetWatts >= 50 && segment.TargetWatts <= 500 && (segment.TargetWatts - 50) % 10 == 0)).IsTrue();
        }
    }

    [Test]
    public async Task Compile_AppliesCadenceOffsetWithoutChangingPowerTargets()
    {
        var workout = new TrainingWorkout("test", "Test", "Test workout", TrainingObjective.Endurance,
            TrainingExperienceLevel.Beginner,
            [new("Work", TimeSpan.FromMinutes(1), 100d, 80, 90, "Ride", true)]);

        var compiled = TrainingWorkoutCompiler.Compile(workout, 200, new(0, 400, 5), cadenceOffsetRpm: 5);

        using (Assert.Multiple())
        {
            await Assert.That(compiled.Segments[0].TargetWatts).IsEqualTo(200);
            await Assert.That(compiled.Segments[0].Definition.MinimumCadenceRpm).IsEqualTo(85);
            await Assert.That(compiled.Segments[0].Definition.MaximumCadenceRpm).IsEqualTo(95);
            await Assert.That(compiled.Definition.Segments[0].MinimumCadenceRpm).IsEqualTo(80);
        }
    }

    [Test]
    public async Task Compile_RejectsCadenceOffsetsOutsideTheSupportedRange()
    {
        var workout = new TrainingWorkout("test", "Test", "Test workout", TrainingObjective.Endurance,
            TrainingExperienceLevel.Beginner,
            [new("Work", TimeSpan.FromMinutes(1), 100d, 80, 90, "Ride", true)]);

        await Assert.That(() => TrainingWorkoutCompiler.Compile(workout, 200, new(0, 400, 5), cadenceOffsetRpm: 16))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => TrainingWorkoutCompiler.Compile(workout, 200, new(0, 400, 5), cadenceOffsetRpm: int.MinValue))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Compile_RejectsOffsetsThatMoveCadenceOutsideThePracticalRange()
    {
        var workout = new TrainingWorkout("test", "Test", "Test workout", TrainingObjective.Endurance,
            TrainingExperienceLevel.Beginner,
            [new("Work", TimeSpan.FromMinutes(1), 100d, 40, 50, "Ride", true)]);

        await Assert.That(() => TrainingWorkoutCompiler.Compile(workout, 200, new(0, 400, 5), cadenceOffsetRpm: -15))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Load_ReturnsEveryObjectiveAndExperienceLevel()
    {
        var catalog = await TrainingCatalog.LoadAsync();

        using (Assert.Multiple())
        {
            await Assert.That(catalog.Workouts.Count).IsEqualTo(35);
            await Assert.That(catalog.Workouts.Select(static workout => workout.Id).Distinct().Count()).IsEqualTo(35);
            await Assert.That(catalog.Plans.Count).IsEqualTo(35);
            await Assert.That(catalog.Plans.Select(static plan => plan.Id).Distinct().Count()).IsEqualTo(35);
        }
    }

    [Test]
    public async Task LoadAsync_ReturnsEveryObjectiveAndExperienceLevel()
    {
        var catalog = await TrainingCatalog.LoadAsync();

        using (Assert.Multiple())
        {
            await Assert.That(catalog.Workouts.Count).IsEqualTo(35);
            await Assert.That(catalog.Plans.Count).IsEqualTo(35);
        }
    }

    [Test]
    public async Task Load_ReturnsAuthoredStructuredWorkouts()
    {
        var catalog = await TrainingCatalog.LoadAsync();

        using (Assert.Multiple())
        {
            await Assert.That(catalog.Workouts.All(static workout => workout.Segments.Count >= 5)).IsTrue();
            await Assert.That(catalog.Workouts.All(static workout => workout.Segments
                .Select(static segment => segment.FunctionalThresholdPowerPercentage)
                .Distinct()
                .Count() >= 3)).IsTrue();
        }
    }

    [Test]
    public async Task Load_UsesLongerAdvancedSweetSpotAndThresholdIntervals()
    {
        var catalog = await TrainingCatalog.LoadAsync();
        var experiencedSweetSpot = catalog.Workouts.Single(static workout =>
            workout.Objective == TrainingObjective.TempoSweetSpot && workout.ExperienceLevel == TrainingExperienceLevel.Experienced);
        var advancedSweetSpot = catalog.Workouts.Single(static workout =>
            workout.Objective == TrainingObjective.TempoSweetSpot && workout.ExperienceLevel == TrainingExperienceLevel.Advanced);
        var experiencedThreshold = catalog.Workouts.Single(static workout =>
            workout.Objective == TrainingObjective.Threshold && workout.ExperienceLevel == TrainingExperienceLevel.Experienced);
        var advancedThreshold = catalog.Workouts.Single(static workout =>
            workout.Objective == TrainingObjective.Threshold && workout.ExperienceLevel == TrainingExperienceLevel.Advanced);

        using (Assert.Multiple())
        {
            await Assert.That(experiencedSweetSpot.Segments.Where(static segment => segment.Name.StartsWith("Sweet spot", StringComparison.Ordinal))
                .All(static segment => segment.Duration >= TimeSpan.FromMinutes(10))).IsTrue();
            await Assert.That(advancedSweetSpot.Segments.Where(static segment => segment.Name.StartsWith("Sweet spot", StringComparison.Ordinal))
                .All(static segment => segment.Duration >= TimeSpan.FromMinutes(12))).IsTrue();
            await Assert.That(experiencedThreshold.Segments.Where(static segment => segment.Name.StartsWith("Under", StringComparison.Ordinal))
                .All(static segment => segment.Duration >= TimeSpan.FromMinutes(8))).IsTrue();
            await Assert.That(advancedThreshold.Segments.Where(static segment => segment.Name.StartsWith("Under", StringComparison.Ordinal))
                .All(static segment => segment.Duration >= TimeSpan.FromMinutes(10))).IsTrue();
        }
    }

    [Test]
    public async Task Load_AnaerobicIntervalsHaveCadenceBuildsAndAtLeastTwoToOneRecovery()
    {
        var catalog = await TrainingCatalog.LoadAsync();
        var anaerobicWorkouts = catalog.Workouts.Where(static workout => workout.Objective == TrainingObjective.Anaerobic);

        foreach (var workout in anaerobicWorkouts)
        {
            for (var index = 0; index < workout.Segments.Count; index++)
            {
                var segment = workout.Segments[index];
                if (!segment.Name.StartsWith("Burst", StringComparison.Ordinal))
                {
                    continue;
                }

                await Assert.That(index > 0 && workout.Segments[index - 1].Name == "Cadence build").IsTrue();

                var nextBurst = index + 1;
                while (nextBurst < workout.Segments.Count
                    && !workout.Segments[nextBurst].Name.StartsWith("Burst", StringComparison.Ordinal))
                {
                    nextBurst++;
                }

                if (nextBurst == workout.Segments.Count)
                {
                    continue;
                }

                var recovery = TimeSpan.Zero;
                for (var recoveryIndex = index + 1; recoveryIndex < nextBurst; recoveryIndex++)
                {
                    recovery += workout.Segments[recoveryIndex].Duration;
                }

                await Assert.That(recovery >= segment.Duration * 2).IsTrue();
            }
        }
    }

    [Test]
    public async Task Load_UsesGearNeutralCuesAndMarksHardWorkAsControlledNotAllOut()
    {
        var catalog = await TrainingCatalog.LoadAsync();
        var cues = catalog.Workouts.SelectMany(static workout => workout.Segments).Select(static segment => segment.Cue);

        await Assert.That(cues.All(static cue =>
            !cue.Contains("shift", StringComparison.OrdinalIgnoreCase)
            && !cue.Contains("gear", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(catalog.Workouts.SelectMany(static workout => workout.Segments)
            .Where(static segment => segment.CountsTowardAdherence)
            .All(static segment => segment.MinimumCadenceRpm is not null || segment.MaximumCadenceRpm is not null)).IsTrue();
        await Assert.That(catalog.Workouts.Where(static workout => workout.Objective == TrainingObjective.Anaerobic)
            .All(static workout => workout.Description.Contains("ERG-paced", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Load_OpensEveryWorkoutWithBroadCadenceThenCadenceLift()
    {
        var catalog = await TrainingCatalog.LoadAsync();

        foreach (var workout in catalog.Workouts)
        {
            var opening = workout.Segments[0];
            var lift = workout.Segments[1];

            using (Assert.Multiple())
            {
                await Assert.That(opening.Name is "Warm up" or "Ease in").IsTrue();
                await Assert.That((opening.MinimumCadenceRpm, opening.MaximumCadenceRpm)).IsEqualTo(((int?)60, (int?)100));
                await Assert.That(lift.Name).IsEqualTo("Cadence lift");
                await Assert.That((lift.MinimumCadenceRpm, lift.MaximumCadenceRpm)).IsEqualTo(((int?)75, (int?)95));
                await Assert.That(lift.Duration).IsEqualTo(TimeSpan.FromMinutes(2));
                await Assert.That(lift.FunctionalThresholdPowerPercentage).IsEqualTo(opening.FunctionalThresholdPowerPercentage);
                await Assert.That(opening.CountsTowardAdherence || lift.CountsTowardAdherence).IsFalse();
            }
        }
    }

    [Test]
    public async Task Load_CalculatesTotalWorkoutDurationFromSegments()
    {
        var catalog = await TrainingCatalog.LoadAsync();

        await Assert.That(catalog.Workouts.All(static workout =>
            workout.TotalDuration == TimeSpan.FromTicks(workout.Segments.Sum(static segment => segment.Duration.Ticks)))).IsTrue();
    }

    [Test]
    public async Task Load_ReturnsProgressivePlansWithResolvableWorkoutReferences()
    {
        var catalog = await TrainingCatalog.LoadAsync();
        var workoutIds = catalog.Workouts.Select(static workout => workout.Id).ToHashSet(StringComparer.Ordinal);

        using (Assert.Multiple())
        {
            await Assert.That(catalog.Plans.All(static plan => plan.Weeks.Count == 8)).IsTrue();
            await Assert.That(catalog.Plans.All(static plan => plan.Weeks.Select(static week => week.Number)
                .SequenceEqual(Enumerable.Range(1, 8)))).IsTrue();
            await Assert.That(catalog.Plans.All(static plan => plan.Weeks.Any(static week => week.Phase == TrainingPlanPhase.Recovery))).IsTrue();
            await Assert.That(catalog.Plans.All(static plan => plan.Weeks
                .Where(static week => week.Phase == TrainingPlanPhase.Recovery)
                .Any(week => week.LowIntensityMinutes < plan.Weeks[0].LowIntensityMinutes))).IsTrue();
            await Assert.That(catalog.Plans.All(plan => plan.Weeks
                .SelectMany(static week => week.WorkoutIds)
                .All(workoutIds.Contains))).IsTrue();
        }
    }
}