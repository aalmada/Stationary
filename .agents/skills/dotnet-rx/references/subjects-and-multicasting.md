# Subjects and Multicasting

## Prefer Composition

A subject is both `IObserver<T>` and `IObservable<T>`. That makes it useful at an imperative-to-reactive boundary, but it also exposes mutable stream state and makes lifetime, serialization, and ownership easier to obscure.

Prefer, in order:

1. An existing factory or adapter such as `FromEventPattern`, `FromAsync`, `Generate`, or `Using`.
2. `Observable.Create` for one custom subscription boundary.
3. A subject only when external code must imperatively publish into a long-lived shared stream.

Never use a subject merely to move values between two parts of one operator chain or to simulate a local variable.

## Subject Types

| Type | New subscriber receives | Terminal behavior | Primary risk |
| --- | --- | --- | --- |
| `Subject<T>` | Future notifications only | Replays terminal notification to late subscribers | Values emitted before subscription are lost |
| `BehaviorSubject<T>` | Required initial value or latest value, then future values | Late subscriber after termination receives only completion/error, not the last value | Confusing it with replay-one after termination |
| `ReplaySubject<T>` | Retained history, then future values | Retains and replays completion/error | Unbounded memory and retained exception state |
| `AsyncSubject<T>` | Nothing until termination; final value only after successful completion | Final value plus completion, or error; an empty source completes without a value | Never produces a value if the source never completes |

Use `ReplaySubject<T>(bufferSize)`, a time window, or both for long-lived sources. An unbounded replay buffer is acceptable only when the complete history has a proven finite bound.

## Encapsulation and Thread Safety

Expose only the observable side:

```csharp
private readonly ISubject<Status> statuses =
    Subject.Synchronize(new Subject<Status>());

public IObservable<Status> Statuses => statuses.AsObservable();
```

- `AsObservable` prevents consumers from casting the exposed value back to the subject implementation.
- `Subject.Synchronize` serializes concurrent calls to `OnNext`, `OnError`, and `OnCompleted`.
- A plain subject's observer side is not safe for concurrent calls.
- Calling `Dispose` on a subject releases subscribers and makes later calls fail; it does not send `OnCompleted`.
- Complete a subject when completion is part of the domain contract, then dispose it only as resource cleanup.

## Publishing Operators

Publishing operators share one upstream subscription through a subject selected for the desired late-subscriber behavior.

| Operator | Subject model | Late-subscriber behavior |
| --- | --- | --- |
| `Publish()` | `Subject<T>` | Future values only |
| `Publish(initialValue)` | `BehaviorSubject<T>` | Current value while active |
| `PublishLast()` | `AsyncSubject<T>` | Final value after completion |
| `Replay(...)` | `ReplaySubject<T>` | Retained values plus terminal notification |
| `Multicast(subject)` | Caller-selected subject | Defined by that subject |

These return `IConnectableObservable<T>`. `Subscribe` attaches observers without starting upstream work; `Connect` starts the shared upstream subscription and returns the connection handle. Dispose that handle to disconnect.

Attach all required observers before `Connect` when synchronous upstream emissions must reach all of them:

```csharp
IConnectableObservable<int> shared = source.Publish();

using IDisposable first = shared.Subscribe(HandleFirst);
using IDisposable second = shared.Subscribe(HandleSecond);
using IDisposable connection = shared.Connect();
```

## Selector Overloads

Use `Publish(shared => ...)`, `Replay(shared => ...)`, or `Multicast(subjectFactory, shared => ...)` when one query needs multiple subscriptions to the same source:

```csharp
IObservable<(int Previous, int Current)> pairs = source.Publish(shared =>
    shared.Zip(shared.Skip(1), (previous, current) => (previous, current)));
```

The selector form creates one subject and one upstream subscription per top-level subscription, connects automatically, and disposes the connection with that subscription. It avoids manual `Connect` races and accidental double subscription inside the query.

## Share the Effectful Layer

Sharing an ingress source does not prevent duplicated work added independently downstream. If multiple consumers derive busy state, status, metrics, or errors from one effectful `FromAsync`, `Do`, resource acquisition, or serialized operation pipeline, construct that execution observable once and share the exact effectful layer before fan-out.

Choose the sharing policy from consumer needs:

- `Publish().RefCount()` shares live results when late consumers may miss prior values.
- `Replay(1).RefCount()` shares the current result when late consumers require one retained value; verify reconnect and terminal replay semantics.
- A selector overload or explicit connection attaches all required consumers before a synchronous source can emit.

Test that one trigger performs the effect once even when every derived state consumer is subscribed. Do not rely on duplicated effects being harmless.

## Automatic Connection Policies

| Policy | Connects | Disconnects | Consequence |
| --- | --- | --- | --- |
| `Publish().RefCount()` | When active subscriber count reaches the threshold, default 1 | When count returns to zero | A later subscriber can start a new upstream execution |
| `RefCount(disconnectDelay, scheduler)` | At threshold | After zero subscribers remain for the delay | Avoids churn during brief subscriber gaps |
| `Publish().AutoConnect(n)` | Once subscriber count reaches `n` | Never automatically | Can retain upstream forever |
| Manual `Connect()` | When application calls it | When connection handle is disposed | Exact lifecycle control, with more ownership burden |

Capture `AutoConnect`'s connection through its callback if shutdown must remain possible. Do not use it for an infinite source without an application-lifetime ownership decision.

`RefCount` disconnect/reconnect is not cache-reset syntax. A connectable observable owns its subject, so retained replay or terminal state can survive connection changes. Test the exact reconnect and reset behavior required by the application instead of assuming a fresh cache.

## Sharing Review

Before publishing a source, answer:

- Is duplicate subscription expensive, stateful, or destructive?
- Should late subscribers miss, receive the latest, or replay history?
- When exactly should upstream start and stop?
- Should a zero-subscriber gap reset state or preserve it?
- What bounds replay memory and retained errors?
- Can synchronous first emissions race later subscribers?
- Is sharing applied before every side effect that must execute once?
- Who owns the connection handle?

Do not solve these questions with nested `Subscribe` calls. Keep the shared lifetime inside an observable returned to the caller so disposal can propagate through the full graph.
