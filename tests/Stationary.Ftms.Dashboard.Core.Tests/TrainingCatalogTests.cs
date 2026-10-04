using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingCatalogTests
{
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