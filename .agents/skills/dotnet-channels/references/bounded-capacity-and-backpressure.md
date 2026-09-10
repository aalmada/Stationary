# Bounded Capacity And Backpressure

## Choose Capacity Deliberately

Prefer a bounded channel unless the backlog is provably finite and controlled. Capacity limits queued item count, not bytes; use realistic payload size and memory budgets when sizing it.

| Full Mode | Behavior | Appropriate For |
| --- | --- | --- |
| `Wait` | `WriteAsync` waits; `TryWrite` returns `false` | Lossless work and backpressure to the producer |
| `DropWrite` | Rejects the incoming item | Best-effort telemetry where new data may be discarded |
| `DropNewest` | Removes the newest queued item for the incoming item | Rare policies where older work is more valuable |
| `DropOldest` | Removes the oldest queued item for the incoming item | Latest-state/sampling updates where stale work is useless |

Treat every drop policy as a product decision. The producer must tolerate loss, and operations must observe it through the `itemDropped` callback, metrics, or both.

## Overload Behavior

- `Wait` applies backpressure. Propagate the `WriteAsync` task to the caller when admission must slow down or fail with the request.
- Do not turn `WriteAsync` into unbounded background tasks to avoid waiting; that moves the unbounded queue into the task scheduler.
- Reject, shed, coalesce, or persist work explicitly when waiting cannot be allowed on a request path.
- Measure producer wait time, queue depth if instrumented by the owner, item age, dropped count, consumer duration, failures, and shutdown drain time.
- Use unbounded channels only when input rate is controlled, all producers are trusted, and memory growth is demonstrably bounded by another mechanism.

## Options

```csharp
var channel = Channel.CreateBounded<WorkItem>(
    new BoundedChannelOptions(capacity: 256)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = false,
        SingleReader = true,
        AllowSynchronousContinuations = false,
    });
```

- Set `SingleWriter`/`SingleReader` to `true` only for actual single-owner paths; incorrect promises are concurrency bugs.
- Leave `AllowSynchronousContinuations` at its conservative default unless inline continuation execution is accepted and profiled. It can reduce scheduling overhead but introduce reentrancy and producer latency.

## Sources

- [Channels: bounding strategies](https://learn.microsoft.com/dotnet/core/extensions/channels#bounding-strategies)
- [BoundedChannelFullMode](https://learn.microsoft.com/dotnet/api/system.threading.channels.boundedchannelfullmode)
