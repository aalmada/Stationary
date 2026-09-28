namespace Stationary.Ftms.Dashboard.Core;

public enum TrainingExperienceLevel
{
    Beginner,
    Developing,
    Intermediate,
    Experienced,
    Advanced,
}

public enum TrainingObjective
{
    Recovery,
    Endurance,
    TempoSweetSpot,
    Threshold,
    Vo2Max,
    CadenceSkills,
    Anaerobic,
}

public sealed record TrainingSegment(
    string Name,
    TimeSpan Duration,
    double FunctionalThresholdPowerPercentage,
    int? MinimumCadenceRpm,
    int? MaximumCadenceRpm,
    string Cue,
    bool CountsTowardAdherence);

public sealed record TrainingWorkout(
    string Id,
    string Name,
    string Description,
    TrainingObjective Objective,
    TrainingExperienceLevel ExperienceLevel,
    IReadOnlyList<TrainingSegment> Segments);

public readonly record struct TrainingPowerCapabilities(int MinimumWatts, int MaximumWatts, int IncrementWatts)
{
    public bool IsValid => MinimumWatts >= 0 && MaximumWatts >= MinimumWatts && IncrementWatts > 0;
}

public sealed record CompiledTrainingSegment(TrainingSegment Definition, int TargetWatts);

public sealed record CompiledTrainingWorkout(TrainingWorkout Definition, IReadOnlyList<CompiledTrainingSegment> Segments);

public static class TrainingWorkoutCompiler
{
    public static CompiledTrainingWorkout Compile(
        TrainingWorkout workout,
        ushort functionalThresholdPowerWatts,
        TrainingPowerCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(workout);
        if (functionalThresholdPowerWatts == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(functionalThresholdPowerWatts));
        }

        if (!capabilities.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        }

        if (workout.Segments.Count == 0)
        {
            throw new ArgumentException("A workout must include at least one segment.", nameof(workout));
        }

        var segments = new CompiledTrainingSegment[workout.Segments.Count];
        for (var index = 0; index < workout.Segments.Count; index++)
        {
            var segment = workout.Segments[index];
            ValidateSegment(segment);
            var target = checked((int)double.Round(
                functionalThresholdPowerWatts * segment.FunctionalThresholdPowerPercentage / 100d,
                MidpointRounding.AwayFromZero));
            if (target < capabilities.MinimumWatts || target > capabilities.MaximumWatts)
            {
                throw new InvalidOperationException($"Segment '{segment.Name}' requires {target} W, outside the bike range.");
            }

            if ((target - capabilities.MinimumWatts) % capabilities.IncrementWatts != 0)
            {
                throw new InvalidOperationException($"Segment '{segment.Name}' requires {target} W, which the bike cannot set exactly.");
            }

            segments[index] = new(segment, target);
        }

        return new(workout, segments);
    }

    private static void ValidateSegment(TrainingSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (string.IsNullOrWhiteSpace(segment.Name) || segment.Duration <= TimeSpan.Zero || segment.FunctionalThresholdPowerPercentage <= 0d)
        {
            throw new ArgumentException("Training segments require a name, positive duration, and positive FTP percentage.", nameof(segment));
        }

        if (segment.MinimumCadenceRpm is > 0 && segment.MaximumCadenceRpm is > 0 && segment.MinimumCadenceRpm > segment.MaximumCadenceRpm)
        {
            throw new ArgumentException("A cadence range must have a minimum no greater than its maximum.", nameof(segment));
        }
    }
}