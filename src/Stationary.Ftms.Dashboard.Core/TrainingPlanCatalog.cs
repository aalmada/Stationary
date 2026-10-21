namespace Stationary.Ftms.Dashboard.Core;

internal static class TrainingPlanCatalog
{
    public static IReadOnlyList<TrainingPlan> Create(IReadOnlyList<TrainingWorkout> workouts)
    {
        ArgumentNullException.ThrowIfNull(workouts);

        var workoutIds = workouts.ToDictionary(
            static workout => (workout.Objective, workout.ExperienceLevel),
            static workout => workout.Id);

        return [..
            Enum.GetValues<TrainingObjective>()
                .SelectMany(objective => Enum.GetValues<TrainingExperienceLevel>()
                    .Select(level => CreatePlan(objective, level, workoutIds)))];
    }

    private static TrainingPlan CreatePlan(
        TrainingObjective objective,
        TrainingExperienceLevel level,
        IReadOnlyDictionary<(TrainingObjective Objective, TrainingExperienceLevel Level), string> workoutIds)
    {
        var primary = GetWorkoutId(objective, level, workoutIds);
        var endurance = GetWorkoutId(TrainingObjective.Endurance, level, workoutIds);
        var recovery = GetWorkoutId(TrainingObjective.Recovery, level, workoutIds);
        var cadence = GetWorkoutId(TrainingObjective.CadenceSkills, level, workoutIds);
        var secondary = GetWorkoutId(GetSecondaryObjective(objective), level, workoutIds);
        var prescription = GetPrescription(level);

        return new(
            Id: $"{objective.ToString().ToLowerInvariant()}-{level.ToString().ToLowerInvariant()}-plan",
            Name: $"{GetObjectiveName(objective)} {GetLevelName(level)}",
            Description: $"Eight-week {GetObjectiveName(objective).ToLowerInvariant()} progression with a foundation, build, deload, and reassessment.",
            Objective: objective,
            ExperienceLevel: level,
            Weeks:
            [
                CreateWeek(1, TrainingPlanPhase.Foundation, prescription, [primary, cadence], "Establish control and finish every interval with reserve."),
                CreateWeek(2, TrainingPlanPhase.Foundation, prescription, [primary, endurance], "Add easy aerobic volume without adding a second hard session."),
                CreateWeek(3, TrainingPlanPhase.Build, prescription, [primary, secondary, endurance], "Build specific work while keeping the easy riding genuinely easy."),
                CreateWeek(4, TrainingPlanPhase.Recovery, prescription with { LowIntensityMinutes = prescription.LowIntensityMinutes * 2 / 3, StrengthSessions = 1 }, [recovery, cadence], "Deload: reduce volume, preserve movement quality, and recover."),
                CreateWeek(5, TrainingPlanPhase.Build, prescription, [primary, secondary, endurance], "Return to quality work after the deload."),
                CreateWeek(6, TrainingPlanPhase.Consolidation, prescription with { LowIntensityMinutes = prescription.LowIntensityMinutes + 30 }, [primary, endurance], "Extend aerobic durability while holding the primary work steady."),
                CreateWeek(7, TrainingPlanPhase.Peak, prescription with { LowIntensityMinutes = prescription.LowIntensityMinutes * 3 / 4, StrengthSessions = 1 }, [primary, secondary], "Sharpen, then reassess FTP after adequate recovery."),
                CreateWeek(8, TrainingPlanPhase.Recovery, prescription with { LowIntensityMinutes = prescription.LowIntensityMinutes / 2, StrengthSessions = 0, RecoveryDays = prescription.RecoveryDays + 1 }, [recovery, endurance], "Absorb the block before beginning another progression.")
            ]);
    }

    private static TrainingPlanWeek CreateWeek(
        int number,
        TrainingPlanPhase phase,
        PlanPrescription prescription,
        IReadOnlyList<string> workoutIds,
        string focus) => new(
            number,
            phase,
            prescription.LowIntensityMinutes,
            prescription.StrengthSessions,
            prescription.RecoveryDays,
            workoutIds,
            focus);

    private static string GetWorkoutId(
        TrainingObjective objective,
        TrainingExperienceLevel level,
        IReadOnlyDictionary<(TrainingObjective Objective, TrainingExperienceLevel Level), string> workoutIds) => workoutIds[(objective, level)];

    private static TrainingObjective GetSecondaryObjective(TrainingObjective objective) => objective switch
    {
        TrainingObjective.Recovery => TrainingObjective.CadenceSkills,
        TrainingObjective.Endurance => TrainingObjective.TempoSweetSpot,
        TrainingObjective.TempoSweetSpot => TrainingObjective.Threshold,
        TrainingObjective.Threshold => TrainingObjective.Vo2Max,
        TrainingObjective.Vo2Max => TrainingObjective.Threshold,
        TrainingObjective.CadenceSkills => TrainingObjective.TempoSweetSpot,
        TrainingObjective.Anaerobic => TrainingObjective.Vo2Max,
        _ => throw new ArgumentOutOfRangeException(nameof(objective)),
    };

    private static PlanPrescription GetPrescription(TrainingExperienceLevel level) => level switch
    {
        TrainingExperienceLevel.Beginner => new(90, 1, 3),
        TrainingExperienceLevel.Developing => new(120, 1, 3),
        TrainingExperienceLevel.Intermediate => new(150, 2, 2),
        TrainingExperienceLevel.Experienced => new(210, 2, 2),
        TrainingExperienceLevel.Advanced => new(270, 2, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static string GetObjectiveName(TrainingObjective objective) => objective switch
    {
        TrainingObjective.Recovery => "Recovery Reset",
        TrainingObjective.Endurance => "Aerobic Foundation",
        TrainingObjective.TempoSweetSpot => "Sweet Spot Builder",
        TrainingObjective.Threshold => "Threshold Builder",
        TrainingObjective.Vo2Max => "VO2 Capacity",
        TrainingObjective.CadenceSkills => "Pedaling Skills",
        TrainingObjective.Anaerobic => "Anaerobic Repeatability",
        _ => throw new ArgumentOutOfRangeException(nameof(objective)),
    };

    private static string GetLevelName(TrainingExperienceLevel level) => level switch
    {
        TrainingExperienceLevel.Beginner => "Start",
        TrainingExperienceLevel.Developing => "Build",
        TrainingExperienceLevel.Intermediate => "Progress",
        TrainingExperienceLevel.Experienced => "Advance",
        TrainingExperienceLevel.Advanced => "Peak",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private readonly record struct PlanPrescription(int LowIntensityMinutes, int StrengthSessions, int RecoveryDays);
}