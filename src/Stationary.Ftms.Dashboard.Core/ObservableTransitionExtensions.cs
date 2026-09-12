using System.Reactive.Linq;

namespace Stationary.Ftms.Dashboard.Core;

public static class ObservableTransitionExtensions
{
    public static IObservable<IReadOnlyList<TransitionRegion<TState>>> ToTransitionRegions<TSource, TState>(
        this IObservable<TSource> source,
        Func<TSource, DateTimeOffset> timestampSelector,
        Func<TSource, TState> stateSelector,
        TimeSpan retention,
        IEqualityComparer<TState>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(timestampSelector);
        ArgumentNullException.ThrowIfNull(stateSelector);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);

        return Observable.Defer(() =>
        {
            var regions = new List<TransitionRegion<TState>>();
            var stateComparer = comparer ?? EqualityComparer<TState>.Default;
            DateTimeOffset? latestTimestamp = null;

            return source.Select(item =>
            {
                var timestamp = timestampSelector(item);
                if (latestTimestamp is { } latest && timestamp < latest)
                {
                    throw new InvalidOperationException("Transition-region timestamps must be monotonic.");
                }

                var state = stateSelector(item);
                if (regions.Count == 0)
                {
                    regions.Add(new(timestamp, timestamp, state));
                }
                else
                {
                    var previous = regions[^1];
                    if (stateComparer.Equals(previous.State, state))
                    {
                        regions[^1] = previous with { End = timestamp };
                    }
                    else
                    {
                        regions[^1] = previous with { End = timestamp };
                        regions.Add(new(timestamp, timestamp, state));
                    }
                }

                latestTimestamp = timestamp;
                var cutoff = timestamp - retention;
                while (regions.Count > 1 && regions[0].End <= cutoff)
                {
                    regions.RemoveAt(0);
                }

                if (regions[0].Start < cutoff)
                {
                    regions[0] = regions[0] with { Start = cutoff };
                }

                return (IReadOnlyList<TransitionRegion<TState>>)[.. regions];
            });
        });
    }
}