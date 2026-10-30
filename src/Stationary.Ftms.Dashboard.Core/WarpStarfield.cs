namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct WarpStar(double X, double Y, double Depth);

public sealed class WarpStarfield
{
    public const double ReferenceSpeedKilometersPerHour = 36d;
    public const double ReferenceDepthPerSecond = 0.6d;
    private const double MinimumDepth = 0.08d;
    private const double DepthRange = 1d - MinimumDepth;
    private readonly WarpStar[] stars = new WarpStar[128];

    public WarpStarfield()
    {
        var random = new Random(5147);
        for (var index = 0; index < stars.Length; index++)
        {
            stars[index] = new(
                (random.NextDouble() - 0.5d) * 1.5d,
                (random.NextDouble() - 0.5d) * 1.5d,
                MinimumDepth + (random.NextDouble() * DepthRange));
        }
    }

    public ReadOnlySpan<WarpStar> Stars => stars;

    // Power-derived speed stays near 15-20 km/h even for easy spinning, so a linear rate makes slow riding look busy.
    // A quadratic rate keeps easy pedalling calm while efforts above the reference speed still warp noticeably faster.
    public static double DepthRatePerSecond(double speedKilometersPerHour)
    {
        if (!(speedKilometersPerHour > 0d))
        {
            return 0d;
        }

        var ratio = speedKilometersPerHour / ReferenceSpeedKilometersPerHour;
        return ReferenceDepthPerSecond * ratio * ratio;
    }

    public void Advance(TimeSpan elapsed, double speedKilometersPerHour)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elapsed.Ticks);
        if (!double.IsFinite(speedKilometersPerHour) || speedKilometersPerHour < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(speedKilometersPerHour));
        }

        var travel = DepthRatePerSecond(speedKilometersPerHour) * elapsed.TotalSeconds % DepthRange;
        if (travel == 0d)
        {
            return;
        }

        for (var index = 0; index < stars.Length; index++)
        {
            var star = stars[index];
            var depth = star.Depth - travel;
            stars[index] = star with { Depth = depth < MinimumDepth ? depth + DepthRange : depth };
        }
    }
}