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
    public async Task Advance_TravelGrowsWithSpeedSquaredAndElapsedTime()
    {
        var slow = new WarpStarfield();
        var fast = new WarpStarfield();
        var longer = new WarpStarfield();
        var initial = slow.Stars[0].Depth;

        slow.Advance(TimeSpan.FromMilliseconds(10), 10d);
        fast.Advance(TimeSpan.FromMilliseconds(10), 20d);
        longer.Advance(TimeSpan.FromMilliseconds(40), 10d);

        using (Assert.Multiple())
        {
            await Assert.That(initial - fast.Stars[0].Depth)
                .IsEqualTo(4d * (initial - slow.Stars[0].Depth)).Within(1e-12);
            await Assert.That(fast.Stars[0].Depth).IsEqualTo(longer.Stars[0].Depth).Within(1e-12);
        }
    }

    [Test]
    public async Task Advance_MovesReferenceDepthPerSecondAtReferenceSpeed()
    {
        var starfield = new WarpStarfield();
        var stars = starfield.Stars.ToArray();
        var index = Array.IndexOf(stars, stars.MaxBy(star => star.Depth));
        var nearest = stars[index];

        starfield.Advance(TimeSpan.FromMilliseconds(100), 36d);

        await Assert.That(nearest.Depth - starfield.Stars[index].Depth).IsEqualTo(0.06d).Within(1e-12);
    }

    [Test]
    public async Task DepthRatePerSecond_KeepsEasySpinningCalm()
    {
        using (Assert.Multiple())
        {
            await Assert.That(WarpStarfield.DepthRatePerSecond(0d)).IsEqualTo(0d);
            await Assert.That(WarpStarfield.DepthRatePerSecond(18d)).IsEqualTo(0.15d).Within(1e-12);
            await Assert.That(WarpStarfield.DepthRatePerSecond(36d)).IsEqualTo(0.6d).Within(1e-12);
        }
    }

    [Test]
    public async Task Advance_RecyclesStarsWithoutChangingTheirDirection()
    {
        var starfield = new WarpStarfield();
        var before = starfield.Stars.ToArray();

        starfield.Advance(TimeSpan.FromSeconds(10), 100d);
        var after = starfield.Stars.ToArray();

        for (var index = 0; index < after.Length; index++)
        {
            using (Assert.Multiple())
            {
                await Assert.That(after[index].Depth).IsGreaterThanOrEqualTo(0.08d);
                await Assert.That(after[index].Depth).IsLessThanOrEqualTo(1d);
                await Assert.That(after[index].X).IsEqualTo(before[index].X);
                await Assert.That(after[index].Y).IsEqualTo(before[index].Y);
            }
        }
    }
}