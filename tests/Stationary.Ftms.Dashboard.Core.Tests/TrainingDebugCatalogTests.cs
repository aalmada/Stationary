using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class TrainingDebugCatalogTests
{
    [Test]
    public async Task Workouts_CompileAtEveryPreferredCadenceAndStayShort()
    {
        foreach (var workout in TrainingDebugCatalog.Workouts)
        {
            foreach (var preferredCadenceRpm in (int[])[TrainingWorkoutCompiler.MinimumPreferredCadenceRpm, 90, TrainingWorkoutCompiler.MaximumPreferredCadenceRpm])
            {
                _ = TrainingWorkoutCompiler.Compile(workout, 200, new(0, 500, 1), preferredCadenceRpm);
            }

            using (Assert.Multiple())
            {
                await Assert.That(workout.Objective).IsEqualTo(TrainingObjective.Diagnostics);
                await Assert.That(workout.TotalDuration).IsLessThanOrEqualTo(TimeSpan.FromMinutes(10));
                await Assert.That(workout.Segments.Skip(1).SkipLast(1).All(static segment => segment.Duration <= TimeSpan.FromMinutes(1))).IsTrue();
            }
        }
    }

    [Test]
    public async Task Workouts_OpenWithCadenceCalibrationSpinUp()
    {
        foreach (var workout in TrainingDebugCatalog.Workouts)
        {
            var compiled = TrainingWorkoutCompiler.Compile(workout, 200, new(0, 500, 1));

            await Assert.That(TrainingCadenceCalibration.IsCalibrationSegment(compiled, 0)).IsTrue();
        }
    }

    [Test]
    public async Task Workouts_CoverPowerStepsCadenceChangesAndJumps()
    {
        var byId = TrainingDebugCatalog.Workouts.ToDictionary(static workout => workout.Id, StringComparer.Ordinal);
        var powerSteps = byId["debug-power-steps"].Segments.Where(static segment => segment.CountsTowardAdherence).ToArray();
        var sweep = byId["debug-cadence-sweep"].Segments.Where(static segment => segment.CountsTowardAdherence).ToArray();

        using (Assert.Multiple())
        {
            await Assert.That(powerSteps.Select(static segment => segment.FunctionalThresholdPowerPercentage).Distinct().Count()).IsGreaterThanOrEqualTo(5);
            await Assert.That(powerSteps.Select(static segment => (segment.MinimumCadenceRpm, segment.MaximumCadenceRpm)).Distinct().Count()).IsEqualTo(1);
            await Assert.That(sweep.Select(static segment => segment.FunctionalThresholdPowerPercentage).Distinct().Count()).IsEqualTo(1);
            await Assert.That(sweep.Select(static segment => segment.MinimumCadenceRpm).Distinct().Count()).IsGreaterThanOrEqualTo(4);
            await Assert.That(byId["debug-big-jumps"].Segments.Select(static segment => segment.FunctionalThresholdPowerPercentage)).Contains(85d);
            await Assert.That(byId["debug-stall-recover"].Segments.Any(static segment => segment.MinimumCadenceRpm is null && !segment.CountsTowardAdherence)).IsTrue();
        }
    }

    [Test]
    public async Task Plan_ReferencesEveryDebugWorkoutAndStaysOutOfTrainingCatalog()
    {
        var plan = TrainingDebugCatalog.Plan;
        var catalog = await TrainingCatalog.LoadAsync();

        using (Assert.Multiple())
        {
            await Assert.That(plan.Objective).IsEqualTo(TrainingObjective.Diagnostics);
            await Assert.That(plan.Weeks.SelectMany(static week => week.WorkoutIds))
                .IsEquivalentTo(TrainingDebugCatalog.Workouts.Select(static workout => workout.Id));
            await Assert.That(catalog.Workouts.Any(static workout => workout.Objective == TrainingObjective.Diagnostics)).IsFalse();
            await Assert.That(catalog.Plans.Any(static candidate => candidate.Objective == TrainingObjective.Diagnostics)).IsFalse();
        }
    }
}