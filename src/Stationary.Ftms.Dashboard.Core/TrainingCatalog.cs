namespace Stationary.Ftms.Dashboard.Core;

public sealed record TrainingCatalogDocument(
    IReadOnlyList<TrainingWorkout> Workouts,
    IReadOnlyList<TrainingPlan> Plans);

public static class TrainingCatalog
{
    public static TrainingCatalogDocument Load()
    {
        var workouts = TrainingWorkoutLibrary.Create();
        var catalog = new TrainingCatalogDocument(workouts, TrainingPlanCatalog.Create(workouts));
        Validate(catalog);
        return catalog;
    }

    public static void Validate(TrainingCatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var expectedVariants = GetExpectedVariants();
        var workoutIds = ValidateWorkouts(catalog.Workouts, expectedVariants);
        ValidatePlans(catalog.Plans, expectedVariants, workoutIds);
    }

    private static HashSet<(TrainingObjective, TrainingExperienceLevel)> GetExpectedVariants() =>
        [.. Enum.GetValues<TrainingObjective>()
            .SelectMany(static objective => Enum.GetValues<TrainingExperienceLevel>().Select(level => (objective, level)))];

    private static HashSet<string> ValidateWorkouts(
        IReadOnlyList<TrainingWorkout> workouts,
        HashSet<(TrainingObjective, TrainingExperienceLevel)> expectedVariants)
    {
        if (workouts.Count != 35)
        {
            throw new InvalidOperationException("The training catalog must include one workout for each objective and experience level.");
        }

        var actual = new HashSet<(TrainingObjective, TrainingExperienceLevel)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workout in workouts)
        {
            if (string.IsNullOrWhiteSpace(workout.Id) || !ids.Add(workout.Id))
            {
                throw new InvalidOperationException("Training workout identifiers must be unique.");
            }

            if (!actual.Add((workout.Objective, workout.ExperienceLevel)))
            {
                throw new InvalidOperationException("The training catalog has duplicate objective and experience-level variants.");
            }

            _ = TrainingWorkoutCompiler.Compile(workout, 200, new(0, 500, 1));
        }

        if (!expectedVariants.SetEquals(actual))
        {
            throw new InvalidOperationException("The training catalog is missing an objective and experience-level variant.");
        }

        return ids;
    }

    private static void ValidatePlans(
        IReadOnlyList<TrainingPlan> plans,
        HashSet<(TrainingObjective, TrainingExperienceLevel)> expectedVariants,
        HashSet<string> workoutIds)
    {
        if (plans.Count != 35)
        {
            throw new InvalidOperationException("The training plan catalog must include one plan for each objective and experience level.");
        }

        var planIds = new HashSet<string>(StringComparer.Ordinal);
        var planVariants = new HashSet<(TrainingObjective, TrainingExperienceLevel)>();
        foreach (var plan in plans)
        {
            ValidatePlan(plan, planIds, planVariants, workoutIds);
        }

        if (!expectedVariants.SetEquals(planVariants))
        {
            throw new InvalidOperationException("The training plan catalog is missing an objective and experience-level variant.");
        }
    }

    private static void ValidatePlan(
        TrainingPlan plan,
        HashSet<string> planIds,
        HashSet<(TrainingObjective, TrainingExperienceLevel)> planVariants,
        HashSet<string> workoutIds)
    {
        if (string.IsNullOrWhiteSpace(plan.Id) || !planIds.Add(plan.Id))
        {
            throw new InvalidOperationException("Training plan identifiers must be unique.");
        }

        if (!planVariants.Add((plan.Objective, plan.ExperienceLevel)))
        {
            throw new InvalidOperationException("The training plan catalog has duplicate objective and experience-level variants.");
        }

        if (plan.Weeks.Count != 8 || !plan.Weeks.Select(static week => week.Number).SequenceEqual(Enumerable.Range(1, 8)))
        {
            throw new InvalidOperationException("Each training plan must provide eight sequential weeks.");
        }

        if (!HasReducedLoadRecoveryWeek(plan.Weeks))
        {
            throw new InvalidOperationException("Each training plan must include a reduced-load recovery week.");
        }

        foreach (var week in plan.Weeks)
        {
            ValidatePlanWeek(week, workoutIds);
        }
    }

    private static bool HasReducedLoadRecoveryWeek(IReadOnlyList<TrainingPlanWeek> weeks) =>
        weeks.Any(static week => week.Phase == TrainingPlanPhase.Recovery)
        && weeks.Where(static week => week.Phase == TrainingPlanPhase.Recovery)
            .Any(week => week.LowIntensityMinutes < weeks[0].LowIntensityMinutes);

    private static void ValidatePlanWeek(TrainingPlanWeek week, HashSet<string> workoutIds)
    {
        if (week.LowIntensityMinutes < 0 || week.StrengthSessions < 0 || week.RecoveryDays < 1 || week.WorkoutIds.Count == 0)
        {
            throw new InvalidOperationException("Training plan prescriptions must be valid.");
        }

        if (week.WorkoutIds.Any(workoutId => !workoutIds.Contains(workoutId)))
        {
            throw new InvalidOperationException("Training plan weeks must reference workouts in the catalog.");
        }
    }
}