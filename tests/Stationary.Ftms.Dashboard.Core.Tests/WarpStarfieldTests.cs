using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class WarpStarfieldTests
{
    [Test]
    public async Task Advance_StoppedBikeLeavesStarsStationary()
    {
        var starfield = new WarpStarfield();
        var before = starfield.Stars.ToArray();

        starfield.Advance(TimeSpan.FromMinutes(1), 0d);

        await Assert.That(starfield.Stars.ToArray()).IsEquivalentTo(before);
    }

    [Test]
    public async Task Advance_MovesTheViewerForwardAtBikeSpeed()
    {
        var starfield = new WarpStarfield();
        var stars = starfield.Stars.ToArray();
        var index = Array.IndexOf(stars, stars.MaxBy(star => star.Depth));

        starfield.Advance(TimeSpan.FromSeconds(1), 36d);

        await Assert.That(stars[index].Depth - starfield.Stars[index].Depth).IsEqualTo(10d).Within(1e-9);
    }

    [Test]
    public async Task Advance_KeepsStarsInsideTheViewWithoutChangingTheirPosition()
    {
        var starfield = new WarpStarfield();
        var before = starfield.Stars.ToArray();

        starfield.Advance(TimeSpan.FromMinutes(3), 40d);
        var after = starfield.Stars.ToArray();

        for (var index = 0; index < after.Length; index++)
        {
            var (x, y) = WarpStarfield.Project(after[index]);
            using (Assert.Multiple())
            {
                await Assert.That(after[index].Depth).IsGreaterThanOrEqualTo(WarpStarfield.NearDepthMetres);
                await Assert.That(after[index].Depth).IsLessThanOrEqualTo(WarpStarfield.FarDepthMetres);
                await Assert.That(double.Abs(x)).IsLessThanOrEqualTo(1d + 1e-9);
                await Assert.That(double.Abs(y)).IsLessThanOrEqualTo(1d + 1e-9);
                await Assert.That(after[index].X).IsEqualTo(before[index].X);
                await Assert.That(after[index].Y).IsEqualTo(before[index].Y);
            }
        }
    }

    [Test]
    public async Task StreakLength_EasyRidingDrawsPointsAndFastRidingDrawsStreaks()
    {
        var starfield = new WarpStarfield();
        var slowest = 0d;
        var fastest = 0d;
        for (var step = 0; step < 200; step++)
        {
            starfield.Advance(TimeSpan.FromMilliseconds(250), 12d);
            foreach (var star in starfield.Stars)
            {
                slowest = double.Max(slowest, WarpStarfield.StreakLength(star, 12d));
                fastest = double.Max(fastest, WarpStarfield.StreakLength(star, 40d));
            }
        }

        using (Assert.Multiple())
        {
            await Assert.That(slowest).IsLessThan(WarpStarfield.MinimumStreakLength);
            await Assert.That(fastest).IsGreaterThan(WarpStarfield.MinimumStreakLength);
        }
    }
}