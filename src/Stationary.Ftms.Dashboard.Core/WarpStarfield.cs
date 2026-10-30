namespace Stationary.Ftms.Dashboard.Core;

// Metres relative to the rider's eye: X to the right, Y up, Depth straight ahead.
public readonly record struct WarpStar(double X, double Y, double Depth);

// Points fixed in the real world, seen through a pinhole camera that moves forward at the bike speed. Apparent motion
// therefore follows real perspective: distant points drift slowly near the centre and only near points sweep outward.
public sealed class WarpStarfield
{
    public const double HorizontalFieldOfViewDegrees = 60d;
    public const double NearDepthMetres = 10d;
    public const double FarDepthMetres = 120d;

    // Streaks show the projected travel during a short exposure, like the motion blur the eye perceives.
    public const double StreakExposureSeconds = 0.1d;

    // Projected travel, in plot half-widths, below which a star is drawn as a point.
    public const double MinimumStreakLength = 0.06d;

    public static readonly double FocalLength =
        1d / double.Tan(double.DegreesToRadians(HorizontalFieldOfViewDegrees) / 2d);

    private readonly WarpStar[] stars = new WarpStar[160];

    public WarpStarfield()
    {
        var random = new Random(5147);
        for (var index = 0; index < stars.Length; index++)
        {
            // Uniform in the viewing pyramid, whose volume up to a depth grows with depth cubed.
            double depth;
            do
            {
                depth = FarDepthMetres * double.Cbrt(random.NextDouble());
            }
            while (depth < NearDepthMetres);

            var halfExtent = depth / FocalLength;
            stars[index] = new(
                ((random.NextDouble() * 2d) - 1d) * halfExtent,
                ((random.NextDouble() * 2d) - 1d) * halfExtent,
                depth);
        }
    }

    public ReadOnlySpan<WarpStar> Stars => stars;

    public static double MetresPerSecond(double speedKilometersPerHour) =>
        speedKilometersPerHour > 0d ? speedKilometersPerHour / 3.6d : 0d;

    // Screen position in plot half-widths from the centre; Y is up.
    public static (double X, double Y) Project(WarpStar star) =>
        (FocalLength * star.X / star.Depth, FocalLength * star.Y / star.Depth);

    // Where the star appeared one exposure ago, before the rider moved closer.
    public static WarpStar StreakTail(WarpStar star, double speedKilometersPerHour) =>
        star with { Depth = star.Depth + (MetresPerSecond(speedKilometersPerHour) * StreakExposureSeconds) };

    public static double StreakLength(WarpStar star, double speedKilometersPerHour)
    {
        var head = Project(star);
        var tail = Project(StreakTail(star, speedKilometersPerHour));
        return double.Hypot(head.X - tail.X, head.Y - tail.Y);
    }

    public void Advance(TimeSpan elapsed, double speedKilometersPerHour)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsed.Ticks);
        if (!double.IsFinite(speedKilometersPerHour) || speedKilometersPerHour < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(speedKilometersPerHour));
        }

        var travel = MetresPerSecond(speedKilometersPerHour) * elapsed.TotalSeconds;
        if (travel == 0d)
        {
            return;
        }

        for (var index = 0; index < stars.Length; index++)
        {
            var star = stars[index];
            // A star leaves the view at the near plane or at the edge of the field of view, then reappears far ahead.
            var closest = ClosestVisibleDepth(star);
            var span = FarDepthMetres - closest;
            if (span <= 0d)
            {
                continue;
            }

            var offset = (star.Depth - closest - travel) % span;
            stars[index] = star with { Depth = closest + (offset < 0d ? offset + span : offset) };
        }
    }

    private static double ClosestVisibleDepth(WarpStar star) =>
        double.Max(NearDepthMetres, FocalLength * double.Max(double.Abs(star.X), double.Abs(star.Y)));
}