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
    IReadOnlyList<TrainingSegment> Segments)
{
    public TimeSpan TotalDuration
    {
        get
        {
            var totalDuration = TimeSpan.Zero;
            foreach (var segment in Segments)
            {
                totalDuration += segment.Duration;
            }

            return totalDuration;
        }
    }
}

public readonly record struct TrainingPowerCapabilities(int MinimumWatts, int MaximumWatts, int IncrementWatts)
{
    public bool IsValid => MinimumWatts >= 0 && MaximumWatts >= MinimumWatts && IncrementWatts > 0;
}

public sealed record CompiledTrainingSegment(TrainingSegment Definition, int TargetWatts);

public sealed record CompiledTrainingWorkout(TrainingWorkout Definition, IReadOnlyList<CompiledTrainingSegment> Segments);

public static class TrainingWorkoutCompiler
{
    public const int MaximumCadenceOffsetRpm = 15;

    public static CompiledTrainingWorkout Compile(
        TrainingWorkout workout,
        ushort functionalThresholdPowerWatts,
        TrainingPowerCapabilities capabilities,
        int cadenceOffsetRpm = 0)
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

        if (cadenceOffsetRpm is < -MaximumCadenceOffsetRpm or > MaximumCadenceOffsetRpm)
        {
            throw new ArgumentOutOfRangeException(nameof(cadenceOffsetRpm));
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
            var requestedWatts = functionalThresholdPowerWatts * segment.FunctionalThresholdPowerPercentage / 100d;
            if (requestedWatts < capabilities.MinimumWatts || requestedWatts > capabilities.MaximumWatts)
            {
                throw new InvalidOperationException($"Segment '{segment.Name}' requires {requestedWatts:F0} W, outside the bike range.");
            }

            var adjustedSegment = segment with
            {
                MinimumCadenceRpm = AdjustCadence(segment.MinimumCadenceRpm, cadenceOffsetRpm),
                MaximumCadenceRpm = AdjustCadence(segment.MaximumCadenceRpm, cadenceOffsetRpm),
            };
            ValidateSegment(adjustedSegment);

            var maximumStep = (capabilities.MaximumWatts - capabilities.MinimumWatts) / capabilities.IncrementWatts;
            var nearestStep = (int)double.Round(
                (requestedWatts - capabilities.MinimumWatts) / capabilities.IncrementWatts,
                MidpointRounding.AwayFromZero);
            var target = capabilities.MinimumWatts + int.Clamp(nearestStep, 0, maximumStep) * capabilities.IncrementWatts;

            segments[index] = new(adjustedSegment, target);
        }

        return new(workout, segments);
    }

    private static int? AdjustCadence(int? cadenceRpm, int offsetRpm) =>
        cadenceRpm is int value ? value + offsetRpm : null;

    private static void ValidateSegment(TrainingSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (string.IsNullOrWhiteSpace(segment.Name)
            || segment.Duration <= TimeSpan.Zero
            || !double.IsFinite(segment.FunctionalThresholdPowerPercentage)
            || segment.FunctionalThresholdPowerPercentage <= 0d)
        {
            throw new ArgumentException("Training segments require a name, positive duration, and positive FTP percentage.", nameof(segment));
        }

        if (segment.MinimumCadenceRpm is > 0 && segment.MaximumCadenceRpm is > 0 && segment.MinimumCadenceRpm > segment.MaximumCadenceRpm)
        {
            throw new ArgumentException("A cadence range must have a minimum no greater than its maximum.", nameof(segment));
        }

        if (segment.MinimumCadenceRpm is < 30 or > 150 || segment.MaximumCadenceRpm is < 30 or > 150)
        {
            throw new ArgumentException("Cadence targets must be between 30 and 150 rpm.", nameof(segment));
        }
    }
}