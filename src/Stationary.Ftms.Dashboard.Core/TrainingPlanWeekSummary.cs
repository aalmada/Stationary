namespace Stationary.Ftms.Dashboard.Core;

public sealed record TrainingPlanWeekSummary(
    string Rides,
    string EasyRiding,
    string EasyRidingDetail,
    string Strength,
    string Rest)
{
    public static TrainingPlanWeekSummary Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    public static TrainingPlanWeekSummary Create(TrainingPlanWeek week, IReadOnlyList<TrainingWorkout> workouts)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(workouts);

        var workoutMinutes = (int)double.Ceiling(workouts.Sum(static workout => workout.TotalDuration.TotalMinutes));
        var missingMinutes = week.LowIntensityMinutes - workoutMinutes;
        return new(
            Rides: workouts.Count switch
            {
                0 => "No workouts this week",
                1 => "Ride the workout below",
                _ => $"Ride all {workouts.Count} workouts below, on separate days",
            },
            EasyRiding: $"{week.LowIntensityMinutes} min total",
            EasyRidingDetail: missingMinutes > 0
                ? $"Workouts last {workoutMinutes} min; add at least {missingMinutes} min of easy riding at talking pace"
                : "Easy parts of the workouts count toward it",
            Strength: week.StrengthSessions switch
            {
                0 => "None this week",
                1 => "1 session (optional)",
                var sessions => $"{sessions} sessions (optional)",
            },
            Rest: week.RecoveryDays == 1 ? "1 day without training" : $"{week.RecoveryDays} days without training");
    }
}