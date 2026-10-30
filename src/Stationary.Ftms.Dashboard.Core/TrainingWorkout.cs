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
    Diagnostics,
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
    // Authored cadence ranges assume a rider whose natural cadence is 90 rpm; they scale proportionally to the rider's preferred cadence.
    public const int ReferenceCadenceRpm = 90;
    public const int MinimumPreferredCadenceRpm = 40;
    public const int MaximumPreferredCadenceRpm = 110;
    private const int MinimumCadenceTargetRpm = 20;
    private const int MaximumCadenceTargetRpm = 150;

    public static CompiledTrainingWorkout Compile(
        TrainingWorkout workout,
        ushort functionalThresholdPowerWatts,
        TrainingPowerCapabilities capabilities,
        int preferredCadenceRpm = ReferenceCadenceRpm)
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

        ValidatePreferredCadence(preferredCadenceRpm);

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

            var adjustedSegment = ScaleCadence(segment, preferredCadenceRpm);
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

    public static bool IsValidPreferredCadence(int preferredCadenceRpm) =>
        preferredCadenceRpm is >= MinimumPreferredCadenceRpm and <= MaximumPreferredCadenceRpm;

    // Rescales cadence ranges from the given segment onward without changing power targets or durations.
    public static CompiledTrainingWorkout RescaleCadence(CompiledTrainingWorkout workout, int preferredCadenceRpm, int fromSegmentIndex)
    {
        ArgumentNullException.ThrowIfNull(workout);
        ValidatePreferredCadence(preferredCadenceRpm);
        ArgumentOutOfRangeException.ThrowIfNegative(fromSegmentIndex);
        if (workout.Definition.Segments.Count != workout.Segments.Count)
        {
            throw new ArgumentException("Only compiled workouts with authored segments can be rescaled.", nameof(workout));
        }

        var segments = new CompiledTrainingSegment[workout.Segments.Count];
        for (var index = 0; index < segments.Length; index++)
        {
            segments[index] = index < fromSegmentIndex
                ? workout.Segments[index]
                : workout.Segments[index] with { Definition = ScaleCadence(workout.Definition.Segments[index], preferredCadenceRpm) };
        }

        return workout with { Segments = segments };
    }

    private static void ValidatePreferredCadence(int preferredCadenceRpm)
    {
        if (!IsValidPreferredCadence(preferredCadenceRpm))
        {
            throw new ArgumentOutOfRangeException(nameof(preferredCadenceRpm));
        }
    }

    // Scaled bands stay above the stall guard's pedalling threshold; a band below it would ask the rider to stall the trainer.
    private const int MinimumScaledCadenceRpm = (int)TrainingCadenceGuard.PedallingCadenceRpm;

    private static TrainingSegment ScaleCadence(TrainingSegment segment, int preferredCadenceRpm)
    {
        var minimum = ScaleCadence(segment.MinimumCadenceRpm, preferredCadenceRpm, double.Floor);
        var maximum = ScaleCadence(segment.MaximumCadenceRpm, preferredCadenceRpm, double.Ceiling);
        var floor = int.Min(MinimumScaledCadenceRpm, segment.MinimumCadenceRpm ?? MinimumScaledCadenceRpm);
        if (minimum < floor)
        {
            // Shift the whole band so its authored width is kept.
            var shift = floor - minimum.Value;
            minimum = floor;
            maximum = maximum is int value ? int.Min(value + shift, MaximumCadenceTargetRpm) : null;
        }

        return segment with { MinimumCadenceRpm = minimum, MaximumCadenceRpm = maximum };
    }

    // Minimums round down and maximums round up so scaled ranges never become narrower than authored.
    private static int? ScaleCadence(int? cadenceRpm, int preferredCadenceRpm, Func<double, double> round) =>
        cadenceRpm is int value
            ? int.Clamp((int)round(value * (double)preferredCadenceRpm / ReferenceCadenceRpm), MinimumCadenceTargetRpm, MaximumCadenceTargetRpm)
            : null;

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

        if (segment.MinimumCadenceRpm is < MinimumCadenceTargetRpm or > MaximumCadenceTargetRpm
            || segment.MaximumCadenceRpm is < MinimumCadenceTargetRpm or > MaximumCadenceTargetRpm)
        {
            throw new ArgumentException("Cadence targets must be between 20 and 150 rpm.", nameof(segment));
        }
    }
}