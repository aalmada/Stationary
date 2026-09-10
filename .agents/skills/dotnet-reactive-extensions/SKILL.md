---
name: dotnet-reactive-extensions
description: "Design, implement, debug, review, and test Reactive Extensions for .NET (Rx.NET) pipelines with System.Reactive. USE FOR: IObservable<T>/IObserver<T>; Observable creation and adapters; LINQ-style operator selection; hot/cold/connectable streams; Subjects; Publish/Replay/RefCount/AutoConnect; schedulers and concurrency; disposal and cancellation; errors/retry; backpressure limits; Task/event/IAsyncEnumerable interop; virtual-time TestScheduler tests; Rx.NET 7 migration and UI packages. DO NOT USE FOR: ReactiveUI application architecture; RxJS/RxJava syntax; simple .NET events with no stream composition; producer-consumer workloads requiring built-in backpressure (prefer Channels or IAsyncEnumerable)."
---

# .NET Reactive Extensions (Rx.NET)

Use `System.Reactive` for composable push-based streams whose values, timing, completion, and errors all matter. Treat ReactiveX documentation as conceptual; verify names and behavior against Rx.NET because implementations differ.

## Anatomy

| File | Purpose | Target Size |
| --- | --- | --- |
| `SKILL.md` | Routing, workflow, invariants, and minimum safe pattern | <100 lines |
| `references/*.md` | Rx.NET-specific semantics, examples, edge cases, and review checks | <200 lines each |

## Workflow

1. Inspect the project target, installed Rx version, and framework-specific packages; load [packages-and-versioning.md](references/packages-and-versioning.md) for Rx.NET 7 migrations.
2. Classify each source by temperature, cardinality, finiteness, producer pace, subscription side effects, owner, terminal behavior, and notification context.
3. Confirm Rx is the right abstraction; prefer `Task<T>` for one result, `IAsyncEnumerable<T>` for async pull, events for simple notification, or `Channel<T>` for bounded producer-consumer flow.
4. Compose built-in factories and operators before writing a subject, custom source, or custom operator.
5. Make ordering and overlap explicit with `Concat`, `Merge`, `Switch`, `Zip`, or `CombineLatest`; operator order changes behavior.
6. Put scheduling decisions at the subscription boundary, inject schedulers used for time, and preserve serialized notifications.
7. Define error, cancellation, and disposal behavior; bound every replay, grouping, queue, retry, and concurrency surface that can grow.
8. Test values, terminal notifications, subscription windows, and disposal with virtual time; never make timing tests sleep.

## Contract

| Concern | Required model |
| --- | --- |
| Grammar | Zero or more `OnNext`, then at most one `OnError` or `OnCompleted`: `(OnNext)*(OnError\|OnCompleted)?` |
| Serialization | A source must not overlap callbacks for one subscription, including reentrant terminal calls |
| Termination | No notification follows `OnError` or `OnCompleted`; either terminal signal ends the sequence |
| Disposal | `Dispose` requests unsubscription; it does not emit `OnCompleted`; no callbacks may occur after `Dispose` returns |
| Evaluation | Most factories and operators are lazy and create one upstream subscription per downstream subscription unless explicitly shared |
| Flow control | Rx.NET has no general cross-thread demand protocol; queued scheduler boundaries and adapters can grow without bound |

## Canonical Pattern

```csharp
IObservable<SearchResult> results = searchTerms
    .Throttle(TimeSpan.FromMilliseconds(250), timerScheduler)
    .DistinctUntilChanged()
    .Select(term => Observable.FromAsync(
        cancellationToken => SearchAsync(term, cancellationToken)))
    .Switch()
    .ObserveOn(resultScheduler);

IDisposable subscription = results.Subscribe(
    onNext: Render,
    onError: ReportFailure);
```

`Throttle` waits for inactivity, `FromAsync` starts per subscription and observes disposal through its token, `Switch` keeps only the newest inner subscription, and `ObserveOn` controls downstream notification context.

## Guardrails

- Always provide an error path at the final subscription boundary.
- Never pass an `async` lambda to an `Action<T>`-based `Subscribe`; model async work with `FromAsync` and flatten it.
- Avoid blocking `First`, `Last`, `Single`, `ForEach`, `Wait`, and `ToEnumerable`; use their observable or awaitable alternatives.
- Prefer `Observable.Create`, `Defer`, `Using`, and operator composition over hand-written `IObservable<T>` implementations.
- Expose `IObservable<T>`, not a mutable subject; use `AsObservable` when the implementation must contain a subject.
- `ObserveOn` queues every notification. When only the newest scheduled value matters, schedule cancellable inner observables and `Switch`; test the exact scheduler behavior.
- Carry owner or generation identity through queued session-sensitive work and revalidate it after asynchronous boundaries.
- Bound `Replay`, `Distinct`, `GroupBy`, `Buffer`, `ObserveOn`, and concurrent inner sequences according to source lifetime and rate.

## Reference Files

| File | Load When |
| --- | --- |
| [references/model-and-lifetimes.md](references/model-and-lifetimes.md) | Defining contracts; reasoning about hot/cold/connectable sources, subscription side effects, serialization, completion, and disposal |
| [references/creating-and-interop.md](references/creating-and-interop.md) | Creating sources; adapting events, tasks, enumerables, async streams, cancellation, and `await` boundaries |
| [references/operator-selection.md](references/operator-selection.md) | Choosing Rx.NET operators and distinguishing flattening, pairing, temporal, filtering, partitioning, and aggregation semantics |
| [references/scheduling-and-concurrency.md](references/scheduling-and-concurrency.md) | Selecting schedulers; placing `SubscribeOn`/`ObserveOn`; handling UI affinity, reentrancy, synchronization, and deadlocks |
| [references/subjects-and-multicasting.md](references/subjects-and-multicasting.md) | Choosing subject types or replacing them; sharing subscriptions with `Publish`, `Replay`, `RefCount`, and `AutoConnect` |
| [references/errors-resources-and-flow.md](references/errors-resources-and-flow.md) | Designing error recovery, retries, cancellation, disposable ownership, resource lifetime, and overload protection |
| [references/testing.md](references/testing.md) | Writing deterministic tests with `TestScheduler`, hot/cold observables, recorded notifications, and subscription assertions |
| [references/custom-operators-and-review.md](references/custom-operators-and-review.md) | Implementing unavoidable custom sources/operators and reviewing Rx code for contract, lifetime, concurrency, and memory defects |
| [references/packages-and-versioning.md](references/packages-and-versioning.md) | Selecting packages/namespaces, migrating to Rx.NET 7, and distinguishing Rx.NET, Ix, async LINQ, and experimental AsyncRx.NET |
| [references/sources.md](references/sources.md) | Rechecking claims against official Rx.NET, ReactiveX, NuGet, and Microsoft documentation |
