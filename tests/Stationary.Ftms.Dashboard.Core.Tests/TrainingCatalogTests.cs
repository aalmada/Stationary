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