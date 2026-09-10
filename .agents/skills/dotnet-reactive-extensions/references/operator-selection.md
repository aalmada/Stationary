# Operator Selection

Operator order is observable behavior. Determine subscription timing, overlap, ordering, completion, error propagation, and retained state before choosing by name.

## ReactiveX Names in Rx.NET

| ReactiveX concept | Rx.NET name |
| --- | --- |
| Map | `Select` |
| Filter | `Where` |
| FlatMap | `SelectMany`, or `Select` followed by `Merge` |
| SwitchMap | `Select` followed by `Switch` |
| ConcatMap | `Select` followed by `Concat` |
| Reduce | `Aggregate` |
| Debounce | `Throttle` |
| Just | `Return` |
| Serialize | `Synchronize` |

Do not copy RxJS or RxJava operator names or edge-case behavior into Rx.NET without checking the Rx.NET API.

## Transform and Filter

| Intent | Operator | Important semantics |
| --- | --- | --- |
| One output per input | `Select` | Selector exceptions become `OnError` |
| Keep matching items | `Where` | Terminal notifications always pass through |
| Filter and narrow type | `OfType<T>` | Drops incompatible values |
| Assert/narrow type | `Cast<T>` | Incompatible value terminates with an error |
| First value or empty | `Take(1)` | Unlike `FirstAsync`, empty input completes normally |
| Require first/last/single | `FirstAsync`, `LastAsync`, `SingleAsync` | Missing or excess values become errors according to operator |
| Stop by count, duration, or predicate | `Take`, `TakeWhile` | Completion disposes the upstream subscription |
| Stop on another observable | `TakeUntil(other)` | `other.OnNext` completes output; `other.OnCompleted` alone has no effect in Rx.NET |
| Stop on cancellation | `TakeUntil(token)` | Token cancellation becomes downstream `OnCompleted` and disposes upstream |
| Global uniqueness | `Distinct` | Retains every seen key until termination |
| Adjacent change detection | `DistinctUntilChanged` | Retains only the latest key |

Avoid the blocking `First`, `Last`, and `Single` variants. Await the corresponding `*Async` observable or continue composing it.

## Flatten and Combine

| Need | Operator | Subscription and ordering model |
| --- | --- | --- |
| Run all inner sources | `Merge` / `SelectMany` | Subscribes concurrently; emits by arrival; completes after outer and all inners complete |
| Bound active inner sources | `Merge(maxConcurrent)` | Queues inner sources beyond the limit until an active one completes |
| Preserve inner sequence order | `Concat` | Subscribes to the next source only after the current one completes |
| Keep only newest inner source | `Switch` | Unsubscribes the previous inner when a new one arrives |
| Race equivalent sources | `Amb` | First source to produce any notification wins; losers are unsubscribed |
| Pair by ordinal position | `Zip` | Buffers the faster side; completes when no further full pair is possible |
| Recompute from latest state | `CombineLatest` | Waits for every input's first value, then emits when any input changes |
| Sample auxiliaries on primary input | `WithLatestFrom` | Emits only when the primary emits, after auxiliaries have values |
| Correlate overlapping lifetimes | `Join` / `GroupJoin` | Duration observables define item windows; retained open windows consume memory |

`Concat` with a hot later source can miss values produced before subscription. `Switch` suppresses stale output but does not guarantee that abandoned work stops unless disposal reaches a cooperative source.

For per-item asynchronous work, use `Select` plus one explicit flattening operator. Do not hide ordering and concurrency in nested `Subscribe` calls.

## Time and Rate

| Need | Operator | Rx.NET behavior |
| --- | --- | --- |
| Wait for inactivity | `Throttle` | Resets its timer on each input; this is debounce semantics in Rx.NET |
| Observe latest periodically | `Sample` | Samples on a schedule; it is not inactivity detection |
| Shift notifications later | `Delay` | Preserves relative timing where possible; errors are forwarded immediately |
| Fail or switch after inactivity/deadline | `Timeout` | `TimeSpan` is an inactivity window; `DateTimeOffset` is an absolute deadline |
| Attach observation time | `Timestamp` | Uses the selected scheduler's clock |
| Attach inter-arrival duration | `TimeInterval` | First interval is measured from subscription |
| Batch by count/time/boundary | `Buffer` | Emits lists; count-based completion may emit a final partial list |
| Stream count/time/boundary partitions | `Window` | Emits nested observables before each partition is complete |

Inject the same scheduler into every time-aware operator in a testable pipeline. Rx timing is best effort in production, not real-time scheduling.

## Accumulate and Decide

| Need | Operator | Completion behavior |
| --- | --- | --- |
| Running state | `Scan` | Emits after each input; works with infinite sources |
| Final state | `Aggregate` | Emits only after source completion |
| Final numeric result | `Count`, `Sum`, `Average`, `Min`, `Max` | Emits only after source completion |
| Source items with extreme key | `MinBy`, `MaxBy` | Rx.NET can emit multiple tied source items after completion |
| Existential/universal test | `Any`, `All` | Short-circuits when the answer is known |
| Ignore values but preserve termination | `IgnoreElements` | Emits only `OnError` or `OnCompleted` |
| Turn notifications into values | `Materialize` | Converts terminal signal into a final `Notification<T>`, then completes |

Do not apply completion-dependent operators to an infinite source unless an earlier operator deliberately makes it finite.

## Retained-State Review

Treat these as memory design decisions, not merely transformations:

| Operator | Potential retained state | Required question |
| --- | --- | --- |
| `Distinct` | Every distinct key | Can key cardinality grow forever? |
| `GroupBy` | One live group per key | How are inactive groups expired? |
| `Replay` | Every retained notification, including terminal errors | What count/time bound is correct? |
| `Zip` | Unmatched items from faster inputs | Can rates diverge indefinitely? |
| `Buffer` | Current and overlapping lists | What bounds count, duration, and overlap? |
| `Window` / `Join` | Active windows and subscriptions | What closes every window? |
| `TakeLast` / `SkipLast` | Tail buffer | Is the requested count or duration bounded? |
| `ObserveOn` | Cross-context notification queue | What happens when downstream is slower? |

Prefer `DistinctUntilChanged` over `Distinct` for state-change streams, bounded `Replay` over unbounded replay, and explicit group/window closing policies for long-lived sources.
