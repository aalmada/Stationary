namespace Stationary.Ftms.Dashboard.Core;

public static class RampFtpProtocol
{
    private static readonly TimeSpan WarmupDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StepDuration = TimeSpan.FromMinutes(1);

    public static CompiledTrainingWorkout Create(TrainingPowerCapabilities capabilities)
    {
        if (!capabilities.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        }

        var stepWatts = GetStepWatts(capabilities.IncrementWatts);
        var startingWatts = Align(double.Clamp(100d, capabilities.MinimumWatts, capabilities.MaximumWatts), capabilities);
        var warmupWatts = Align(double.Max(capabilities.MinimumWatts, startingWatts * 0.5d), capabilities);
        var segments = new List<CompiledTrainingSegment>
        {
            new(new("Warm up", WarmupDuration, warmupWatts, null, null, "Easy spin", false), warmupWatts),
        };

        for (var target = startingWatts; target <= capabilities.MaximumWatts; target += stepWatts)
        {
            segments.Add(new(
                new("Ramp", StepDuration, target, null, null, "Ride until you choose to stop", true),
                target));
        }

        var definition = new TrainingWorkout(
            "ftp-ramp-test",
            "FTP ramp test",
            "A measured-power ramp estimate. End when you can no longer sustain the step.",
            TrainingObjective.Threshold,
            TrainingExperienceLevel.Intermediate,
            [.. segments.Select(static segment => segment.Definition)]);
        return new(definition, segments);
    }

    private static int GetStepWatts(int incrementWatts)
    {
        var multiplier = int.Max(1, (int)double.Round(20d / incrementWatts, MidpointRounding.AwayFromZero));
        return checked(incrementWatts * multiplier);
    }

    private static int Align(double watts, TrainingPowerCapabilities capabilities)
    {
        var aligned = capabilities.MinimumWatts + (int)double.Round((watts - capabilities.MinimumWatts) / capabilities.IncrementWatts, MidpointRounding.AwayFromZero) * capabilities.IncrementWatts;
        return int.Clamp(aligned, capabilities.MinimumWatts, capabilities.MaximumWatts);
    }
}