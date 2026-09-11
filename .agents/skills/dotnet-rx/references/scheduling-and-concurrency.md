# Scheduling and Concurrency

## Mental Model

Rx.NET is free-threaded, not automatically multithreaded. Most operators call the next observer directly on the thread that delivered the upstream notification. A pipeline made only of `Where`, `Select`, `Scan`, and similar operators normally executes as nested method calls.

Schedulers control three independent concerns:

- **Context:** where work runs.
- **Timing:** when work runs.
- **Clock:** what `Now` means.

An operator uses a scheduler only when its implementation requires scheduling or when an overload receives one. There is no ambient "current Rx scheduler" inherited through a chain.

## Scheduler Selection

| Scheduler | Behavior | Use / avoid |
| --- | --- | --- |
| `ImmediateScheduler.Instance` | Runs work inline; delayed work can block the caller | Tiny immediate work; avoid for timers, recursion, or unknown workloads |
| `CurrentThreadScheduler.Instance` | Uses a per-thread trampoline queue; drains before returning | Iterative synchronous sources; avoids deep recursive stacks but still occupies the caller |
| `DefaultScheduler.Instance` | Uses platform default concurrency/timers, normally the CLR thread pool | Default for time-based operators and general deferred work |
| `TaskPoolScheduler.Default` | Schedules through TPL's default task scheduler | Explicit thread-pool work; benchmark before preferring it to `DefaultScheduler` |
| `EventLoopScheduler` | Queues all work on one dedicated thread | Serialized thread affinity without a UI loop; dispose it to stop the thread |
| `NewThreadScheduler.Default` | Creates a new thread for top-level scheduled work | Rare long-running isolation; avoid for routine item processing |
| `SynchronizationContextScheduler` | Posts to a supplied `SynchronizationContext` | UI or host affinity when a context is explicitly available |
| `DispatcherScheduler` / `ControlScheduler` | Uses a WPF dispatcher or Windows Forms control | UI integration; requires the Rx.NET 7 framework package |
| `TestScheduler` | Runs queued work under manually controlled virtual time | Deterministic tests, never production |

Prefer the concrete singleton properties shown above over obsolete `Scheduler.ThreadPool`, `Scheduler.TaskPool`, and `Scheduler.NewThread` shortcuts.

## `SubscribeOn` vs `ObserveOn`

| Operator | Moves | Does not guarantee |
| --- | --- | --- |
| `SubscribeOn(scheduler)` | Upstream subscription and unsubscription side effects | The context of later notifications from a source with its own scheduler |
| `ObserveOn(scheduler)` | Every downstream `OnNext`, `OnError`, and `OnCompleted` | Upstream subscription context or parallel processing |

`ObserveOn` affects operators below its position; multiple calls can introduce multiple context boundaries. Each boundary requires scheduling and usually a queue, so add only boundaries required by correctness.

`SubscribeOn` is useful when subscription itself performs blocking initialization. It is not a substitute for proper asynchronous I/O, and it does not override a timer or external source that chooses its own notification context.

## Ownership of Scheduling

The top-level consumer knows its thread-affinity constraints and should make final scheduling decisions.

- Library methods should normally return scheduler-neutral observables.
- Accept `IScheduler` when a library owns time-based behavior that tests must virtualize.
- Keep `SubscribeOn` and final `ObserveOn` close to `Subscribe` so their policy is visible.
- Do not hide `ObserveOnDispatcher`, thread-pool scheduling, or blocking waits inside domain services.
- Pass one injected scheduler to all related `Timer`, `Delay`, `Throttle`, `Timeout`, `Timestamp`, and `TimeInterval` calls.

For a UI boundary with blocking subscription setup:

```csharp
IDisposable subscription = Observable
    .Defer(service.GetUpdates)
    .SubscribeOn(backgroundScheduler)
    .ObserveOn(uiScheduler)
    .Subscribe(
        onNext: view.Apply,
        onError: view.ShowError);
```

Put CPU-heavy transformations before the final UI `ObserveOn`. If `GetUpdates` is already truly asynchronous, use `FromAsync`/composition instead of moving it to a worker merely because it returns later.

## Serialization, Reentrancy, and Parallelism

Schedulers do not waive the observable grammar. A single subscription must still receive non-overlapping callbacks.

- Rx combining operators serialize concurrent upstream notifications before forwarding them.
- Use `Subject.Synchronize` when multiple threads call a subject's observer side.
- Use `source.Synchronize()` to protect downstream from a source that may overlap notifications.
- Use the observer synchronization overload that prevents reentrancy when callbacks can synchronously cause new emissions.
- Do not hold application locks while calling `OnNext`; subscriber code is arbitrary and can reenter.

Scheduling work on a pool does not make stateful callbacks safe to run in parallel. Express per-item overlap through nested observables and `Merge(maxConcurrent)` so subscription, error, and cancellation lifetimes remain visible.

## Queue and Latency Costs

`ObserveOn` decouples upstream from downstream with a notification queue. If upstream is faster, latency and memory can grow even though callbacks remain serialized.

Before adding a scheduler boundary, answer:

1. Which thread-affinity or latency requirement needs it?
2. What is the maximum upstream rate and burst size?
3. Can values be sampled, aggregated, dropped, or rejected?
4. How is the queue drained during shutdown?
5. How will tests control the scheduler and assert ordering?

Do not add `ObserveOn` as a generic fix for concurrency. Fix contract violations at the source, and add context switches only where consumers require them.

## Latest-Only Context Crossing

`ObserveOn` preserves every notification by queueing it. That is correct for lossless state transitions, but it can deliver stale telemetry, progress, or previews long after newer values arrive. When the domain explicitly requires only the newest pending UI value, make each scheduled delivery a cancellable inner observable:

```csharp
IObservable<Snapshot> latestOnUi = snapshots
    .Select(snapshot => Observable.Return(snapshot, uiScheduler))
    .Switch();
```

`Switch` disposes the previous scheduled inner; cancellable scheduler work that has not started is removed. It cannot preempt a callback already running. Use this pattern only for replaceable values, never for commands, audit events, deltas, or other lossless transitions.

Queue-sensitive work should carry an owner, session, request, or generation identity. Revalidate that identity after the scheduler hop before mutating durable state; switching one pipeline cannot retract work already queued by a different boundary.

## Deadlock Avoidance

- Avoid blocking `First`, `Last`, `Single`, `ForEach`, `Wait`, and `ToEnumerable` on asynchronous or UI-affine sources.
- Await `FirstAsync`, `SingleAsync`, `ToTask`, or `ForEachAsync` instead of blocking a thread.
- Never block a context needed by the source to subscribe, emit, or dispose.
- Do not use `ImmediateScheduler` for periodic work; delayed scheduling may block and periodic cancellation can become impossible.
- Dispose `EventLoopScheduler` and custom schedulers according to their owning scope.

The strongest default is no extra concurrency. Introduce it only when measurement or an affinity requirement justifies the additional queues, ordering choices, and shutdown paths.
