# Concurrency And Consumption

## Producer And Consumer Topology

| Topology | Configure | Consequence |
| --- | --- | --- |
| One producer, one consumer | `SingleWriter = true`, `SingleReader = true` | FIFO processing with simplest ownership |
| Many producers, one consumer | `SingleWriter = false`, `SingleReader = true` | One serialized processing stream |
| One producer, many consumers | `SingleWriter = true`, `SingleReader = false` | Items are distributed, not broadcast |
| Many producers, many consumers | Both `false` | Throughput increases; completion and ordering require coordination |

A channel is a work queue, not a pub/sub topic. Multiple consumers compete for items; each item is delivered to one consumer only. Give every subscriber a separate channel or use a broker/observable design when fan-out is required.

## Completion Ownership

- Assign one component as the completion owner. With multiple producers, await all producer tasks before it calls `TryComplete`.
- Do not let an individual producer complete a shared writer unless it owns the entire producer set.
- If producer failure should stop the pipeline, coordinate `TryComplete(exception)` once and observe the reader completion fault.

## Process Sequentially Or In Parallel

- A single consumer preserves channel dequeue order and is easiest to reason about.
- Multiple consumers improve throughput but completion order is nondeterministic. Do not use them when effects must be globally ordered.
- For bounded parallelism while retaining one dequeue owner, read sequentially and limit processing with a semaphore or a bounded worker pool. Define per-key ordering if required.
- Use `ReadAllAsync` for straightforward processing. Use `WaitToReadAsync` plus `TryRead` for explicit batch draining and to reduce await overhead under load.

## Safe Batch Drain

```csharp
while (await reader.WaitToReadAsync(cancellationToken))
{
    while (reader.TryRead(out WorkItem item))
    {
        await ProcessAsync(item, cancellationToken);
    }
}
```

Do not hold a read item indefinitely while waiting for unrelated external work. Bound concurrent processing and ensure every failure path either retries intentionally, records the failure, or stops the service.

## Sources

- [Channels: multiple producers and consumers](https://learn.microsoft.com/dotnet/core/extensions/channels#multiple-producers-and-consumers)
- [Channels: consumer patterns](https://learn.microsoft.com/dotnet/core/extensions/channels#consumer-patterns)
