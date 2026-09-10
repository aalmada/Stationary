# Contracts And API

## Channel Contract

Define these decisions before constructing a channel:

| Concern | Decide Explicitly |
| --- | --- |
| Item | Immutable message/work item, payload size, ownership, serialization boundary |
| Delivery | Lossless, latest-only, best-effort, retryable, or durable requirement |
| Ordering | Per channel, per key, or no ordering guarantee across parallel consumers |
| Ownership | Who writes, who completes, who reads, and who owns cancellation |
| Failure | Retry, discard, compensate, dead-letter externally, or stop the pipeline |

Expose only a writer to producers and a reader to consumers. Keep the mutable channel private to the queue owner so no caller can complete or consume unexpectedly.

## Writer APIs

| API | Use | Behavior |
| --- | --- | --- |
| `TryWrite` | Non-waiting fast path | `false` when full under `Wait` mode or after completion |
| `WriteAsync` | Required delivery/backpressure | Waits for capacity and observes cancellation/completion |
| `WaitToWriteAsync` | Batch/custom write loop | Waits until write might succeed; call `TryWrite` afterward |
| `TryComplete` | Coordinated shutdown | Idempotently ends writes, optionally with an exception |
| `Complete` | Sole owner ending a finite stream | Ends writes; throws if already completed |

Use `WriteAsync` for normal lossless delivery. Do not use an `async` fire-and-forget producer; it can hide cancellation, faults, and backpressure.

## Reader APIs

| API | Use | Behavior |
| --- | --- | --- |
| `TryRead` | Non-waiting drain | Returns `false` when no item is available |
| `ReadAsync` | Read one item | Throws `ChannelClosedException` when completed and empty |
| `ReadAllAsync` | Process until complete | Drains items then ends; accepts a cancellation token |
| `WaitToReadAsync` | Drain batches | Returns `false` after completion and drain |
| `Completion` | Supervision | Completes after the writer completes and buffered items drain |

For a finite queue, prefer `await foreach (T item in reader.ReadAllAsync(cancellationToken))`. For batching, use `WaitToReadAsync` followed by a `TryRead` loop. Do not use an infinite `ReadAsync` loop unless closed-channel exceptions are intentional control flow.

## Completion And Cancellation

- The queue owner completes the writer after all producers finish. Readers continue until buffered items drain.
- Passing an exception to `TryComplete` faults readers after buffered items drain. Reserve this for a producer/pipeline fault that should terminate consumption.
- Cancellation stops the current wait or enumeration; it does not complete the writer or discard buffered items automatically.
- Observe `Completion` when a supervisor must distinguish successful drain from a fault.

## Sources

- [Channels](https://learn.microsoft.com/dotnet/core/extensions/channels)
- [ChannelWriter](https://learn.microsoft.com/dotnet/api/system.threading.channels.channelwriter-1)
- [ChannelReader](https://learn.microsoft.com/dotnet/api/system.threading.channels.channelreader-1)
