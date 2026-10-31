namespace Stationary.Ftms.Dashboard.Core;

public static class HeartRateWorkoutFactory
{
    private static readonly TimeSpan WarmUpDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CoolDownDuration = TimeSpan.FromMinutes(5);

    public static IReadOnlyList<TrainingWorkout> CreateZoneWorkouts(HeartRateZoneProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Zones.Count < 2)
        {
            throw new ArgumentException("Zone workouts require at least two heart-rate zones.", nameof(profile));
        }

        var recovery = profile.Zones[0];
        var endurance = profile.Zones[1];
        var recoveryMaximum = recovery.MaximumBeatsPerMinute
            ?? throw new ArgumentException("Recovery zone workouts need a bounded first heart-rate zone.", nameof(profile));
        var recoveryMinimum = ushort.Min(ushort.Max(80, recovery.MinimumBeatsPerMinute), recoveryMaximum);
        var enduranceMaximum = endurance.MaximumBeatsPerMinute
            ?? throw new ArgumentException("Endurance zone workouts need a bounded second heart-rate zone.", nameof(profile));

        return [
            CreateZoneWorkout(
                "heart-rate-recovery",
                "Heart-rate recovery",
                "Easy spin held in your recovery zone. Power moves only within a conservative safety range.",
                recoveryMinimum,
                recoveryMaximum,
                40d,
                60d,
                TimeSpan.FromMinutes(15),
                "Keep the effort light and let heart rate guide the resistance."),
            CreateZoneWorkout(
                "heart-rate-zone-2",
                "Zone 2 aerobic base",
                "Steady aerobic work held in your configured endurance heart-rate zone.",
                endurance.MinimumBeatsPerMinute,
                enduranceMaximum,
                40d,
                80d,
                TimeSpan.FromMinutes(30),
                "Settle into the zone; the bike power target adjusts gradually to follow heart rate."),
        ];
    }

    public static TrainingWorkout CreateGeneralHealthWorkout(
        ushort maximumHeartRate,
        ushort restingHeartRate,
        bool vigorous,
        TimeSpan duration)
    {
        ValidateDuration(duration);
        var range = vigorous
            ? HeartRateZoneProfile.GetHeartRateReserveRange(maximumHeartRate, restingHeartRate, 60, 89)
            : HeartRateZoneProfile.GetHeartRateReserveRange(maximumHeartRate, restingHeartRate, 40, 59);
        return new TrainingWorkout(
            vigorous ? "health-cardio-vigorous" : "health-cardio-moderate",
            vigorous ? "Vigorous health cardio" : "Moderate health cardio",
            "General exercise-intensity guidance for apparently healthy adults, not a medical prescription.",
            TrainingObjective.HeartRate,
            TrainingExperienceLevel.Beginner,
            [
                new(
                    "Heart-rate guided ride",
                    duration,
                    vigorous ? 65d : 45d,
                    null,
                    null,
                    "The app adjusts power gradually to stay within the heart-rate reserve range.",
                    true,
                    HeartRateTrainingTarget.ForRange(
                        range.Minimum,
                        range.Maximum,
                        30d,
                        vigorous ? 100d : 85d)),
            ])
        {
            Kind = TrainingWorkoutKind.GeneralHealth,
        };
    }

    public static TrainingWorkout CreateClinicianPrescribedWorkout(
        ushort minimumHeartRate,
        ushort maximumHeartRate,
        int maximumPowerWatts,
        TimeSpan duration)
    {
        if (minimumHeartRate is < 30 or > 250)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumHeartRate));
        }

        if (maximumHeartRate is < 30 or > 250 || maximumHeartRate < minimumHeartRate)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumHeartRate));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPowerWatts);
        ValidateDuration(duration);
        return new TrainingWorkout(
            "clinician-prescribed-heart-rate",
            "Clinician-prescribed heart-rate session",
            "Uses the heart-rate and power limits entered from your care team's individual prescription.",
            TrainingObjective.HeartRate,
            TrainingExperienceLevel.Beginner,
            [
                new(
                    "Prescribed range",
                    duration,
                    40d,
                    null,
                    null,
                    "Follow your clinician's instructions and stop if you feel unwell.",
                    true,
                    HeartRateTrainingTarget.ForRange(
                        minimumHeartRate,
                        maximumHeartRate,
                        0d,
                        100d,
                        maximumPowerWatts)),
            ])
        {
            Kind = TrainingWorkoutKind.ClinicianPrescribed,
        };
    }

    private static TrainingWorkout CreateZoneWorkout(
        string id,
        string name,
        string description,
        ushort minimumHeartRate,
        ushort maximumHeartRate,
        double minimumPowerPercentage,
        double maximumPowerPercentage,
        TimeSpan targetDuration,
        string cue) =>
        new(
            id,
            name,
            description,
            TrainingObjective.HeartRate,
            TrainingExperienceLevel.Beginner,
            [
                new("Warm up", WarmUpDuration, 40d, null, null, "Start with an easy spin.", false),
                new(
                    "Heart-rate target",
                    targetDuration,
                    (minimumPowerPercentage + maximumPowerPercentage) / 2d,
                    null,
                    null,
                    cue,
                    true,
                    HeartRateTrainingTarget.ForRange(
                        minimumHeartRate,
                        maximumHeartRate,
                        minimumPowerPercentage,
                        maximumPowerPercentage)),
                new("Cool down", CoolDownDuration, 35d, null, null, "Ease off gradually.", false),
            ])
        {
            Kind = TrainingWorkoutKind.HeartRateZone,
        };

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1) || duration > TimeSpan.FromHours(4))
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }
}