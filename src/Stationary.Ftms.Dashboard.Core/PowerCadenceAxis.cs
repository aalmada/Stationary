namespace Stationary.Ftms.Dashboard.Core;

public static class PowerCadenceAxis
{
    // Scale to the targets ridden so far plus the next one: the axis only grows during a workout, so the scale stays
    // steady, yet open-ended protocols such as the ramp test do not scale to the bike's advertised maximum.
    public static double? MaximumWatts(CompiledTrainingWorkout? workout, int segmentIndex)
    {
        if (workout is not { Segments.Count: > 0 } || segmentIndex < 0)
        {
            return null;
        }

        var visibleSegments = RampFtpProtocol.VisibleSegmentCount(segmentIndex, workout.Segments.Count);
        return workout.Segments.Take(visibleSegments).Max(static segment => segment.TargetWatts);
    }
}