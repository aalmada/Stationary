# Creating Sources and Interoperability

## Creation Matrix

| Need | Rx.NET API | Key behavior |
| --- | --- | --- |
| One value, empty, never, or error | `Return`, `Empty`, `Never`, `Throw` | Precise cardinality and terminal behavior |
| Integer/state-driven sequence | `Range`, `Generate` | Honors scheduler and disposal better than a hand-written loop |
| Time-driven values | `Timer`, `Interval`, timed `Generate` | Accept an `IScheduler`; `Interval` is infinite until disposed |
| Per-subscription factory/state | `Defer` | Invokes the factory for every subscription |
| Callback or custom async source | `Create` | Callback returns cleanup or accepts a cancellation token |
| Disposable resource per subscription | `Using` | Disposes on completion, error, or unsubscription |
| .NET event | `FromEventPattern` / `FromEvent` | Adds on subscription and removes on disposal |
| Existing task | `task.ToObservable()` | Shares that already-created task across subscribers |
| New task per subscription | `Observable.FromAsync` | Calls the task factory for each subscription |
| Synchronous enumerable | `source.ToObservable(scheduler)` | Pushes enumeration; may otherwise occupy the subscribing thread |

Prefer the narrowest factory that expresses the source. `Create` is powerful, but every custom callback owns grammar, serialization, cancellation, and cleanup concerns.

## Per-Subscription Async Work

`FromAsync` is lazy and can connect disposal to cooperative task cancellation:

```csharp
IObservable<Response> responses = Observable.FromAsync(
    cancellationToken => client.SendAsync(request, cancellationToken));
```

Each subscription starts a new call. Disposing cancels the token, but the operation stops only if it observes cancellation. By contrast:

```csharp
Task<Response> task = client.SendAsync(request);
IObservable<Response> sharedResult = task.ToObservable();
```

The task starts before Rx subscription and runs once. Unsubscription cannot retroactively make a non-cancellable task factory.

Use `Observable.Start` only when eager execution is intentional: it invokes the delegate when `Start` is called and caches its eventual result for subscribers. Wrap work in `Defer` or use `FromAsync` for subscription-time execution.

## Async Work per Item

Never write `source.Subscribe(async item => await HandleAsync(item))`. The lambda binds to `Action<T>`, becomes `async void`, escapes Rx error handling, and lets work overlap without an explicit policy.

Project each item to an observable and choose flattening semantics:

```csharp
IObservable<Result> operations = source.Select(item =>
    Observable.FromAsync(cancellationToken => ProcessAsync(item, cancellationToken)));

IObservable<Result> sequential = operations.Concat();
IObservable<Result> boundedParallel = operations.Merge(maxConcurrent: 4);
IObservable<Result> latestOnly = operations.Switch();
```

- `Concat` waits for each operation to complete.
- `Merge` overlaps operations and emits by completion/arrival order.
- `Switch` unsubscribes the previous operation when a newer one appears; cancellation reaches it only when the inner source honors disposal.

## Event Adapters

Prefer add/remove delegates over the reflection-based event-name overload, especially for trimming or Native AOT:

```csharp
IObservable<FileSystemEventArgs> changes = Observable
    .FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(
        handler => watcher.Changed += handler,
        handler => watcher.Changed -= handler)
    .Select(eventPattern => eventPattern.EventArgs);
```

Keep ownership explicit. If the adapter creates the event source, use `Defer` plus `Using` or `Finally` so each subscription releases it. If the event source is shared externally, do not dispose it from one subscription.

## Custom Async Sources

Use the cancellation-aware `Observable.Create` overload when a multi-value async operation cannot be expressed as existing observables:

```csharp
IObservable<Item> ReadItems() => Observable.Create<Item>(
    async (observer, cancellationToken) =>
    {
        await foreach (Item item in ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            observer.OnNext(item);
        }

        observer.OnCompleted();
    });
```

The async overload converts callback exceptions to `OnError` and requests token cancellation on unsubscription. Avoid work after a terminal call, and do not emit concurrently from parallel tasks.

## Leaving `IObservable<T>`

| Boundary | Behavior | Main hazard |
| --- | --- | --- |
| `await source` | Waits for completion and returns the last item | Empty source faults; infinite source never returns; each await subscribes |
| `source.FirstAsync()` then `await` | Returns the first item and unsubscribes | Empty source faults |
| `source.ToTask(token)` | Task completes with the last item | Requires `System.Reactive.Threading.Tasks`; cancellation unsubscribes |
| `source.ForEachAsync(action)` | Runs a synchronous action per item and awaits termination | The action is not an async backpressure callback |
| `source.ToEnumerable()` | Blocks `MoveNext` until data arrives and queues faster input | Deadlock and unbounded queue risk |
| `ToArray` / `ToList` | Emits one collection after source completion | Infinite or very large sources never finish or exhaust memory |

When the same exception object can cross out of Rx and be rethrown more than once, place `ResetExceptionDispatchState` before `await`, `ToTask`, or another throwing boundary. This matters for cached/replayed errors and `Observable.Throw` with a reused exception instance.

## Async Streams

`IAsyncEnumerable<T>` is pull-based; `IObservable<T>` is push-based. Conversion changes flow-control semantics.

- Observable-to-async-enumerable adapters must queue notifications that arrive before `MoveNextAsync` consumes them. Treat the queue as potentially unbounded unless the adapter documents a bound.
- Async-enumerable-to-observable adapters enumerate once per subscription and use disposal to cancel the enumerator.
- These adapters live in the Interactive Extensions packages in the `dotnet/reactive` repository, not in the core `System.Reactive` package. Current repository work places LINQ-adjacent adapters in the `System.Interactive.Async` package as methods on `System.Linq.AsyncEnumerableEx`; older versions may expose them from `System.Linq.Async`. Inspect the resolved package API.
- Prefer staying in one abstraction across the processing pipeline. Convert once at an integration boundary rather than alternating between push and pull.
