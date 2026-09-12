using System.Reactive.Linq;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Core.Tests;

public sealed class ObservableTransitionExtensionsTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Test]
    public async Task ToTransitionRegions_MergesAdjacentEqualStates()
    {
        var snapshots = await new[]
            {
                new Sample(Start, "Z1"),
                new Sample(Start.AddSeconds(1), "Z1"),
                new Sample(Start.AddSeconds(2), "Z1"),
            }
            .ToObservable()
            .ToTransitionRegions(static sample => sample.Timestamp, static sample => sample.State, TimeSpan.FromMinutes(5))
            .ToList();

        var regions = snapshots[^1];
        using (Assert.Multiple())
        {
            await Assert.That(regions).Count().IsEqualTo(1);
            await Assert.That(regions[0]).IsEqualTo(new TransitionRegion<string>(Start, Start.AddSeconds(2), "Z1"));
        }
    }

    [Test]
    public async Task ToTransitionRegions_ClosesPreviousRegionAtTransition()
    {
        var snapshots = await new[]
            {
                new Sample(Start, "Z1"),
                new Sample(Start.AddSeconds(3), "Z2"),
                new Sample(Start.AddSeconds(5), "Z2"),
            }
            .ToObservable()
            .ToTransitionRegions(static sample => sample.Timestamp, static sample => sample.State, TimeSpan.FromMinutes(5))
            .ToList();

        var regions = snapshots[^1];
        using (Assert.Multiple())
        {
            await Assert.That(regions).Count().IsEqualTo(2);
            await Assert.That(regions[0]).IsEqualTo(new TransitionRegion<string>(Start, Start.AddSeconds(3), "Z1"));
            await Assert.That(regions[1]).IsEqualTo(new TransitionRegion<string>(Start.AddSeconds(3), Start.AddSeconds(5), "Z2"));
        }
    }

    [Test]
    public async Task ToTransitionRegions_TrimsRegionsOutsideRetentionWindow()
    {
        var snapshots = await new[]
            {
                new Sample(Start, "Z1"),
                new Sample(Start.AddSeconds(2), "Z2"),
                new Sample(Start.AddSeconds(8), "Z3"),
            }
            .ToObservable()
            .ToTransitionRegions(static sample => sample.Timestamp, static sample => sample.State, TimeSpan.FromSeconds(5))
            .ToList();

        var regions = snapshots[^1];
        using (Assert.Multiple())
        {
            await Assert.That(regions).Count().IsEqualTo(2);
            await Assert.That(regions[0]).IsEqualTo(new TransitionRegion<string>(Start.AddSeconds(3), Start.AddSeconds(8), "Z2"));
            await Assert.That(regions[1]).IsEqualTo(new TransitionRegion<string>(Start.AddSeconds(8), Start.AddSeconds(8), "Z3"));
        }
    }

    [Test]
    public async Task ToTransitionRegions_CreatesIndependentStatePerSubscription()
    {
        var source = new[]
            {
                new Sample(Start, "Z1"),
                new Sample(Start.AddSeconds(1), "Z2"),
            }
            .ToObservable()
            .ToTransitionRegions(static sample => sample.Timestamp, static sample => sample.State, TimeSpan.FromMinutes(5));

        var first = await source.ToList();
        var second = await source.ToList();

        await Assert.That(second[^1]).IsEquivalentTo(first[^1]);
    }

    private readonly record struct Sample(DateTimeOffset Timestamp, string State);
}