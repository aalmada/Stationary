# Custom Operators and Review

## Implementation Order

Use the least powerful mechanism that solves the requirement:

1. Compose built-in operators.
2. Wrap that composition in an extension method.
3. Add `Defer` for per-subscription state or factory work.
4. Use `Create` for an external callback/async boundary that existing adapters cannot represent.
5. Implement `IObservable<T>` directly only when profiling or an unavailable primitive justifies the full contract burden.

Do not inherit Rx internal operator types or depend on implementation details. They are not the public extension surface.

## Composed Operator Pattern

Validate arguments immediately, then return a lazy composition:

```csharp
public static IObservable<TResult> SelectConcat<TSource, TResult>(
    this IObservable<TSource> source,
    Func<TSource, IObservable<TResult>> selector)
{
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(selector);

    return source.Select(selector).Concat();
}
```

This preserves upstream error/disposal behavior and makes sequential inner-subscription semantics obvious. Prefer a domain-specific name that describes the result rather than publishing aliases for operators Rx already provides.

## Per-Subscription State

An extension method executes when the query is built; `Defer` executes for every subscription. Use it when state or resource selection must not be shared:

```csharp
public static IObservable<State> TrackState(
    this IObservable<Event> source,
    State initialState)
{
    ArgumentNullException.ThrowIfNull(source);

    return Observable.Defer(() =>
        source.Scan(initialState, static (state, @event) =>
            state.Apply(@event)));
}
```

If `State` is mutable, create a fresh instance inside `Defer`; passing one mutable instance into `Scan` would share it across subscriptions.

## `Observable.Create` Checklist

Before returning a custom source, verify every item:

- The callback is lazy and creates all mutable state per subscription.
- The callback returns cleanup immediately, or uses the async overload's cancellation token.
- Cleanup is idempotent and handles partial setup.
- At most one terminal notification is sent.
- No notification occurs after a terminal signal or after `Dispose` returns.
- Calls into one observer never overlap, including reentrant calls.
- Exceptions from the `Create` subscription delegate or its returned task become `OnError`; exceptions thrown by downstream observer callbacks are neither translated nor swallowed.
- Cancellation stops scheduled, callback, event, and task work as far upstream as possible.
- Any scheduler required for time is an explicit argument.
- Synchronous termination during `Subscribe` cannot race assignment of the returned disposable.

Prefer `FromEventPattern` for conventional events and `FromAsync` for one-result task factories. Both already implement details that custom `Create` code frequently gets wrong.

## Public API Design

- Return `IObservable<T>`, not `Subject<T>`, `ReplaySubject<T>`, or another mutable implementation.
- Use `AsObservable` when a subject must remain private.
- Document temperature, replay, cardinality, completion, error, and subscription side effects.
- Document whether multiple subscriptions duplicate external work.
- Do not bake UI or thread-pool scheduling into a domain API; accept an `IScheduler` only for owned temporal behavior.
- Keep cancellation behavior explicit: early disposal, `TakeUntil(token)` completion, and task cancellation are observably different.
- Never return `null`; use `Observable.Empty<T>()` for an empty sequence.

## Review by Failure Mode

| Symptom | Likely defect | Check |
| --- | --- | --- |
| Duplicate HTTP/device/file work | Cold source subscribed more than once | Find fan-out and decide whether to use a selector-based `Publish` or explicit sharing |
| Missing first values | Hot source started before all observers attached | Check construction/start order and use `IConnectableObservable<T>` when start must be explicit |
| Stale async results | `SelectMany`/`Merge` used where latest wins | Use `Select(...).Switch()` and verify inner cancellation |
| Results out of order | Concurrent flattening chosen implicitly | Use `Concat` for order or carry sequence IDs if parallelism is required |
| UI cross-thread exception | Wrong downstream notification context | Place one final `ObserveOn(uiScheduler)` |
| UI deadlock | Blocking operator waits on the same context needed by source | Replace blocking extraction with observable/awaitable composition |
| Memory grows with runtime | Unbounded queue/cache/key/window | Audit `ObserveOn`, `Replay`, `Distinct`, `GroupBy`, `Zip`, `Buffer`, and adapters |
| Retry storm | Infinite immediate `Retry` around a persistent failure | Bound attempts, filter exceptions, delay on an injected scheduler, honor cancellation |
| Errors disappear | Missing final error handler or disposal before late task failure | Trace `OnError`, task ownership, and `TaskObservationOptions` |
| Cleanup never runs | Nested subscription escaped the returned graph | Replace nested `Subscribe` with flattening and return one composed observable |
| Callback overlap/corruption | Source or subject violates serialization | Fix producer synchronization or wrap the unsafe boundary |
| Stream unexpectedly restarts | `RefCount` reached zero then reconnected | Define connection ownership/reset semantics explicitly |
| Old session mutates new state | Queued work carries values but no owner/generation identity | Carry identity through the queue and revalidate after scheduler/async boundaries |
| Effect executes once per derived property | Sharing occurs before, not after, downstream side effects | Share the exact effectful observable before status/result fan-out |

## Performance Review

Correct semantics come first. Then measure the actual hot path.

- Remove unnecessary `ObserveOn` boundaries; each adds scheduling and queueing.
- Prefer specialized built-in operators over equivalent general compositions only after measurement.
- Avoid allocation-heavy closures in high-rate selectors; use static lambdas where practical.
- Bound concurrent inner sources before increasing scheduler parallelism.
- Consider `Window` plus incremental aggregation when `Buffer` would retain large batches.
- Measure subscription setup, steady-state throughput, latency distribution, allocations, and teardown separately.
- Benchmark with realistic source rates and scheduler boundaries; synchronous `Range` benchmarks do not model external event streams.

## Completion Criteria

1. State source temperature, cardinality, lifetime, and notification context.
2. Show why each combining/flattening operator matches ordering and overlap requirements.
3. Identify every subscription and connection owner.
4. Identify every queue, replay buffer, group table, retry loop, and concurrency bound.
5. Test values, terminal signals, subscriptions, disposal, and virtual time.
6. Run a real concurrency test if any producer can call from multiple threads.
7. Switch owners or generations while work is queued and verify stale callbacks cannot mutate replacement state.
