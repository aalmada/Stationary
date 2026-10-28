namespace Stationary.Ftms.Dashboard.Core;

public enum TrainingPlanPhase
{
    Foundation,
    Build,
    Consolidation,
    Peak,
    Recovery,
}

public sealed record TrainingPlanWeek(
    int Number,
    TrainingPlanPhase Phase,
    int LowIntensityMinutes,
    int StrengthSessions,
    int RecoveryDays,
    IReadOnlyList<string> WorkoutIds,
    string Focus);

public sealed record TrainingPlan(
    string Id,
    string Name,
    string Description,
    TrainingObjective Objective,
    TrainingExperienceLevel ExperienceLevel,
    IReadOnlyList<TrainingPlanWeek> Weeks);