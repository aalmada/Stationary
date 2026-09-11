# Observable Model and Lifetimes

## Choose the Abstraction First

| Requirement | Prefer | Reason |
| --- | --- | --- |
| One eventual result | `Task<T>` / `ValueTask<T>` | One completion, built-in `await` and cancellation conventions |
| Consumer-paced async sequence | `IAsyncEnumerable<T>` | Pull-based iteration naturally limits reads to consumer demand |
| Bounded producer-consumer queue | `Channel<T>` | Explicit capacity, wait/drop policies, and async reads/writes |
| Simple in-process notification | .NET event | Familiar subscription model without an operator dependency |
| Multiple live sources, temporal logic, or declarative composition | `IObservable<T>` with Rx.NET | Push-based composition over values, time, completion, and errors |

Do not choose Rx merely to make a method asynchronous. Choose it when a sequence's future values and their temporal relationships form the problem.

## Notification Contract

For each subscription, a well-behaved source follows this grammar:

```text
(OnNext)*(OnError|OnCompleted)?
```

- `OnNext` may occur zero or more times.
- `OnError` and `OnCompleted` are mutually exclusive terminal notifications.
- Infinite or idle sources may never terminate.
- A source must wait for each callback to return before invoking another callback on the same observer. This includes terminal callbacks and same-thread reentrancy.
- A source may use different threads for successive notifications, but it must still serialize them.
- The guarantee is per subscription. Subscribing the same observer to multiple sources does not serialize those independent sources.
- Observer callbacks should not throw. Rx error operators handle `OnError` notifications, not exceptions escaping a downstream observer.

## Temperature and Subscription Effects

| Kind | Start rule | Subscriber view | Typical examples |
| --- | --- | --- | --- |
| Cold | Starts separately for each subscription | Each subscriber gets an independent execution, often from the beginning | `Range`, `Generate`, `Defer`, `FromAsync`, `Timer` |
| Hot | Runs independently of subscriptions | Subscribers see only notifications produced while attached | UI events, device feeds, `Subject<T>` |
| Cold-then-hot | Retains or queues an initial history, then becomes live | A late consumer may catch up to a retained position, then follow live data | Durable broker/event-log adapters |
| Connectable | Subscriptions attach observers; `Connect` starts one shared upstream subscription | Subscribers can be attached before production starts | `Publish`, `Replay`, `Multicast` |

Temperature is behavior, not a type annotation. Most operators preserve the source's subscription behavior. `Publish`, `Replay`, `RefCount`, and `AutoConnect` deliberately change it.

Record these facts for every external source:

- What starts on `Subscribe`?
- Does each subscriber repeat side effects such as HTTP calls, file watchers, or device sessions?
- Can a late subscriber miss data?
- Can the source complete, and what does completion mean?
- Which context emits notifications?
- What does unsubscription cancel, and how quickly?

## Subscription Lifetime

`Subscribe` returns the handle for one subscription, not ownership of the observable itself.

- Natural `OnCompleted` or `OnError` must release that subscription's resources; consumers are not required to dispose an already terminated subscription.
- Dispose a subscription when its owner ends before the source naturally terminates. Common owners are a request, view, service, or application scope.
- `Dispose` requests unsubscription and normally initiates upstream cancellation. It does not call `OnCompleted`.
- Cancellation of underlying work can be slow or ineffective. Once `Dispose` returns, however, a compliant source must not call that subscription's observer again.
- Keep shutdown signaling separate from unsubscription when the consumer must observe final shutdown status. Disposing first permits those final notifications to be dropped.
- Operator chains propagate natural termination downstream and early disposal upstream.

Prefer finite lifetime operators such as `Take`, `TakeUntil`, `Timeout`, or a cancellation-token overload when the lifetime is part of the stream's meaning. Use an owning `CompositeDisposable` when several independent subscriptions share a lifecycle.

## State and Laziness

State belongs to a subscription unless sharing is intentional.

- Use `Observable.Defer` to allocate state or select a source at subscription time.
- Use `Observable.Create` when a callback API cannot be expressed with an existing adapter.
- Keep mutable accumulators inside `Defer`, `Create`, `Scan`, or an operator callback that Rx instantiates per subscription.
- Do not close over one mutable collection and accidentally share it across subscribers.
- Expect each downstream subscription to create a new upstream subscription until a publishing operator says otherwise.

## Serialization and Reentrancy

Rx operators preserve the serialization contract when their inputs obey it. At unsafe boundaries:

- Serialize concurrent calls into a subject with `Subject.Synchronize(subject)`.
- Serialize an untrusted observable's outgoing calls with `source.Synchronize()`.
- Use the reentrancy-protecting observer overload when same-thread callbacks can recursively emit.
- Do not assume `lock` alone prevents same-thread reentrancy; monitor locks are reentrant.
- Keep callbacks short. A direct source cannot make progress while `OnNext` is still running.

`Subject.Synchronize` protects calls entering a writable subject. `Observable.Synchronize` protects notifications leaving an arbitrary observable source.

## Flow-Control Reality

Direct synchronous `OnNext` calls provide only local, implicit pressure because the producer must wait for the callback to return. This is not a general demand protocol.

`ObserveOn`, event adapters, task adapters, broker clients, and other asynchronous boundaries may queue faster than downstream can consume. `Buffer` changes item shape; it does not slow the producer. `Sample` and `Throttle` intentionally discard values; they do not provide lossless backpressure.

For every high-rate or unbounded source, choose one explicit policy:

- Slow or pause the real producer through its native API.
- Bound concurrency with `Merge(maxConcurrent)`.
- Bound storage and define overflow as wait, drop-oldest, drop-newest, sample, aggregate, reject, or fail.
- Cross into a bounded `Channel<T>` when lossless producer-consumer coordination is required.
- Partition only with an eviction policy; `GroupBy` otherwise retains every key until termination.
