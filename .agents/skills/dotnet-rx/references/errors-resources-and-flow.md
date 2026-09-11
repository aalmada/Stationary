# Errors, Resources, and Flow Control

## Error Semantics

`OnError` is data-plane termination, not a resumable exception. After an error, the source cannot emit more values or complete. Recovery operators create a continuation by subscribing to another observable.

- Supply `onError` at every final `Subscribe` boundary unless the observer type handles it.
- Do not throw from `OnNext`, `OnError`, or `OnCompleted`; downstream exceptions are not converted back into upstream `OnError` notifications.
- Operator callbacks such as `Select` predicates/selectors are inside the pipeline; their exceptions normally become `OnError`.
- Errors produced after unsubscription may be dropped. Do not start unobserved fire-and-forget work inside a source.
- Use `Do(onError: ...)` for diagnostics without changing error semantics; keep the callback non-throwing.

## Recovery Operators

| Operator | Behavior | Use carefully because |
| --- | --- | --- |
| `Catch<T, TException>(handler)` | Replaces a matching failure with the observable returned by the handler | Broad `Exception` catches can hide defects |
| `Catch(fallbacks)` | Moves to the next source after each failure | Exception details are discarded |
| `Retry(count)` | Resubscribes after failure; `count` is total attempts | Side effects and previously emitted values repeat |
| `Retry()` | Retries forever | Persistent failures can create a tight infinite loop |
| `RetryWhen(handler)` | Error stream controls retry, completion, or replacement error | Signal completion swallows the last failure unless designed otherwise |
| `OnErrorResumeNext` | Concatenates sources while ignoring errors | Silent data loss and hidden failures |
| `Timeout` | Emits `TimeoutException` or switches to a fallback | Inactivity timeout differs from an absolute deadline |

Catch only expected failures and preserve unexpected ones:

```csharp
IObservable<Settings> settings = LoadSettings().Catch(
    (FileNotFoundException exception) =>
        exception.FileName == expectedPath
            ? Observable.Return(Settings.Default)
            : Observable.Throw<Settings>(exception));
```

Build retries around a deferred, cancellable operation so each resubscription starts a fresh attempt. Bound attempts, restrict retryable exception types, add scheduler-driven delay/jitter where appropriate, and make duplicated side effects idempotent. Test the final exhausted path; do not accidentally turn failure into completion.

## Exception Dispatch State

Rx conveys exceptions as values through `OnError`, so an exception object may never have been thrown before it later crosses into `await`, `ToTask`, `ToEnumerable`, or another boundary that throws it.

Use `ResetExceptionDispatchState` immediately before that boundary when the same exception instance can be thrown more than once, particularly with:

- `Observable.Throw` built from a reused exception object.
- `Replay` or `ReplaySubject`, which retain and resend one error object.
- Repeated awaits or task conversions over a cached failing sequence.

Do not add the operator to pipelines that remain entirely within Rx; it exists for the throwing boundary.

## Resource Lifetime

| Tool | Use |
| --- | --- |
| `Observable.Using` | Create one `IDisposable` per subscription and bind it to completion, error, or unsubscription |
| `Finally` | Run cleanup/telemetry for completion, error, or early disposal |
| `Disposable.Create` | Turn an idempotent cleanup action into `IDisposable` |
| `CompositeDisposable` | Own several subscriptions/resources as one scope |
| `SerialDisposable` | Replace a current resource and dispose the previous value immediately |
| `SingleAssignmentDisposable` | Publish a subscription handle once while handling synchronous termination races |
| `MultipleAssignmentDisposable` | Replace the current resource without disposing the prior value |
| `CancellationDisposable` | Bridge `Dispose` to `CancellationTokenSource.Cancel` |
| `ContextDisposable` / `ScheduledDisposable` | Marshal disposal to a context or scheduler |
| `RefCountDisposable` | Hold an underlying resource until primary and dependent leases end |

`CompositeDisposable.Remove` and `Clear` dispose removed items. Items added after the composite has been disposed are disposed immediately.

Rx.NET 6.1+ provides `DisposeWith` in `System.Reactive.Disposables.Fluent`:

```csharp
private readonly CompositeDisposable lifetime = new();

source.Subscribe(Handle, ReportFailure)
    .DisposeWith(lifetime);
```

The lifecycle owner must eventually dispose `lifetime`. Do not create a composite merely to hide subscriptions that no reachable owner can release.

## Completion vs Cancellation

| Event | Downstream sees | Upstream action |
| --- | --- | --- |
| Source calls `OnCompleted` | `OnCompleted` | Natural teardown |
| Source calls `OnError` | `OnError` | Natural teardown |
| Consumer disposes | Usually no terminal notification | Unsubscribe/cancel as supported |
| `TakeUntil(token)` token cancels | `OnCompleted` | Disposes upstream |
| `FromAsync` subscription is disposed | No terminal notification required | Cancels the supplied token |

Choose `TakeUntil(cancellationToken)` when cancellation should be represented as successful stream completion. Dispose directly when the observer simply no longer wants notifications.

## Serialized Async Work and Error Isolation

For an application-long hot request stream whose operations must run in order, project each request to one cancellable inner observable, recover expected failures inside that inner, then concatenate:

```csharp
IObservable<OperationOutcome> outcomes = requests
    .Select(request => Observable
        .FromAsync(token => ExecuteAsync(request, token))
        .Catch<OperationOutcome, Exception>(error =>
            Observable.Return(OperationOutcome.Failed(error))))
    .Concat();
```

An error escaping an inner terminates `Concat` and unsubscribes from the request stream. An outer `Catch` can replace that failed subscription, but it does not automatically resume the original hot queue. Decide which failures become values and which deliberately terminate orchestration.

## Producer Teardown Order

Do not dispose writable ingress while external producers can still call it; subject disposal makes later calls fail. Stop admission, cancel and await producers/consumers, detach callbacks, dispose downstream subscriptions, then complete the ingress when completion is part of its contract. Dispose owned subjects and concurrency primitives only after callers and waiters have exited.

## Overload Protection

Rx.NET has no general cross-thread backpressure protocol. Audit every asynchronous boundary and stateful operator.

| Pressure point | Failure mode | Typical policy |
| --- | --- | --- |
| `ObserveOn` | Unbounded notification queue | Reduce before boundary, batch, sample, or use a bounded transport |
| `SelectMany` / `Merge` | Unbounded active tasks/subscriptions | `Select` then `Merge(maxConcurrent)` |
| `Replay` | Unbounded retained history | Count/time-bounded replay |
| `Distinct` | Unbounded key set | `DistinctUntilChanged`, finite window, or explicit eviction |
| `GroupBy` | Unbounded groups | Duration-based grouping or domain eviction |
| `Zip` | Faster-source queue growth | Align rates, sample, bound upstream, or choose latest-value semantics |
| `Buffer` | Large lists and overlapping copies | Cap count/time/overlap and define empty/partial-batch handling |
| Observable-to-async-enumerable adapter | Push values queued behind pull consumer | Bound before conversion or use `Channel<T>` |

Dropping is a valid policy only when the domain permits it. Name the policy in code and tests: latest-only (`Switch`), periodic latest (`Sample`), inactivity result (`Throttle`), bounded batch (`Buffer`), or explicit channel overflow behavior.

## Resource and Failure Review

- Every subscription, connection, subject, and dedicated scheduler has an owner.
- Every resource-producing source handles completion, error, and early disposal.
- Every retry is bounded or deliberately application-long, cancellable, delayed, filtered, and idempotent.
- Every terminal subscriber handles errors without throwing.
- Every application-long serialized operation stream isolates recoverable inner failures without terminating future requests.
- Every unbounded state or queue has a documented upper bound or overflow policy.
- Tests cover cancellation during setup, active work, queued work, and reconnect delays.
