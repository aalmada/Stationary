# Testing Rx.NET

## Package and Scope

Add `Microsoft.Reactive.Testing` at the same major/minor version as `System.Reactive`. The package provides `TestScheduler`, recorded notifications, and test observables.

The Rx.NET maintainers publish this package primarily for their own operator tests and explicitly do not promise API compatibility. Pin its version, isolate test helpers around it, and review release notes before upgrades.

Virtual-time tests verify observable scheduling semantics. They do not prove thread safety, fairness, real timer precision, or behavior under actual scheduler contention; add focused integration/stress tests for those concerns.

## Design for Testability

- Accept `IScheduler` in code that owns time-dependent behavior.
- Use that scheduler for every `Timer`, `Interval`, `Delay`, `Throttle`, `Sample`, `Timeout`, `Timestamp`, and time-based publishing overload in the tested path.
- Keep final UI/background scheduling at a composition boundary where tests can substitute schedulers.
- Avoid direct `DateTimeOffset.Now`, `Task.Delay`, `Thread.Sleep`, static dispatcher access, and default time-based overloads inside testable stream logic.
- Inject one clock/scheduler abstraction unless distinct clocks are a real domain requirement.

```csharp
public static IObservable<string> StableTerms(
    IObservable<string> terms,
    TimeSpan quietPeriod,
    IScheduler scheduler) =>
    terms
        .Throttle(quietPeriod, scheduler)
        .DistinctUntilChanged();
```

## `TestScheduler` Controls

| Member | Effect |
| --- | --- |
| `Clock` | Current virtual time in `TimeSpan` ticks |
| `AdvanceTo(time)` | Executes queued work through an absolute virtual time |
| `AdvanceBy(delta)` | Executes work while advancing relative to the current clock |
| `Start()` | Drains scheduled work, advancing to each due time |
| `Stop()` | Stops the current drain after the running work item |
| `Start(create, created, subscribed, disposed)` | Creates, subscribes, records, and disposes at explicit virtual times |
| `CreateObserver<T>()` | Records timestamped notifications |
| `CreateColdObservable(...)` | Schedules notifications relative to each subscription |
| `CreateHotObservable(...)` | Schedules notifications at absolute virtual times |

The parameterless `Start(create)` convention uses `Created = 100`, `Subscribed = 200`, and `Disposed = 1000`. Prefer explicit times in reusable tests when those defaults would obscure intent.

Work scheduled for the current virtual instant is moved forward so the scheduler can make progress. Avoid assertions that depend accidentally on time zero; use named timestamps and inspect the actual subscription window.

## Recorded Notification Test

```csharp
using Microsoft.Reactive.Testing;
using static Microsoft.Reactive.Testing.ReactiveTest;

var scheduler = new TestScheduler();
var source = scheduler.CreateHotObservable(
    OnNext(210, "a"),
    OnNext(220, "ab"),
    OnNext(260, "abc"),
    OnCompleted<string>(400));

ITestableObserver<string> results = scheduler.Start(() =>
    source.Throttle(TimeSpan.FromTicks(30), scheduler));

Recorded<Notification<string>>[] expected =
[
    OnNext(250, "ab"),
    OnNext(290, "abc"),
    OnCompleted<string>(400),
];
```

Compare `results.Messages` with `expected` using the project's assertion library. Also assert `source.Subscriptions`; correct values with the wrong subscribe/dispose window still indicate a lifetime defect.

## Hot vs Cold Test Inputs

| Input | Recorded times mean | What a late subscription does |
| --- | --- | --- |
| Cold | Offsets from each subscription | Receives the full scripted sequence on its own timeline |
| Hot | Absolute scheduler times | Misses notifications before its subscription time |

Use a cold input for per-subscription work such as an HTTP-operation model. Use a hot input for UI events, device feeds, and shared external timelines. The distinction should match production behavior, not whichever makes the test easier.

## What to Assert

For each operator or pipeline, cover the relevant dimensions:

- `OnNext` values and exact order.
- `OnCompleted` versus `OnError`, including error type and identity when relevant.
- Subscription start/end times on every test source.
- Early disposal before the next scheduled value.
- Empty, single-item, finite, infinite, and never-ending inputs.
- Synchronous completion during subscription.
- An error before and after useful values.
- Hot values immediately before and at subscription boundaries.
- Ties: multiple actions scheduled for the same tick run in scheduling order.
- Reentrancy and concurrent-source cases outside virtual time where necessary.

## Targeted Semantic Tests

| Feature | Essential cases |
| --- | --- |
| `Throttle` | Replacement within quiet period; final pending value; error timing; disposal |
| `Timeout` | Value just before deadline; inactivity failure; fallback; source completion |
| `Switch` | Old inner unsubscribed at new-inner arrival; stale value suppressed; outer completion waits for current inner |
| `Concat` | Next inner subscribed only after completion; error prevents later subscription |
| `Merge(maxConcurrent)` | Active-subscription cap; queued inner starts after completion; error tears down all |
| `RefCount` | First connect; last disconnect; delayed reconnect; synchronous source termination |
| `Replay` | Count/time trimming; late subscribers; retained completion/error |
| `TakeUntil(other)` | Other `OnNext` completes output; other completion alone has no effect; other error propagates |
| Cancellation | Already-cancelled token; cancellation during setup/work; no callbacks after disposal returns |

## Debugging Without Changing Semantics

- Use `Do` to log `OnNext`, `OnError`, and `OnCompleted` at selected boundaries.
- Use `Materialize` to turn all notifications into inspectable values.
- Use `Timestamp(testScheduler)` or `TimeInterval(testScheduler)` to record virtual timing.
- Record subscription/disposal side effects with `Defer` and `Finally` in a test double.
- Do not add `ObserveOn` merely to make a race disappear; identify and test the actual serialization boundary.

## Test Review

- No wall-clock sleeping or generous timing tolerances in unit tests.
- Every production scheduler used by the behavior is replaceable in the test.
- Expected notifications include terminal events, not only values.
- Tests assert upstream subscription lifetimes and cancellation when the operator promises them.
- A separate test exercises true multithreading when correctness depends on concurrent calls, locks, or reentrancy.
