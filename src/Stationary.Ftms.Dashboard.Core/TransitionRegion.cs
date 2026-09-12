namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct TransitionRegion<TState>(
    DateTimeOffset Start,
    DateTimeOffset End,
    TState State);