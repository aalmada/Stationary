---
name: dotnet-channels
description: "Design, implement, debug, test, and review in-process producer-consumer pipelines with System.Threading.Channels. USE FOR: Channel<T>, ChannelReader<T>, ChannelWriter<T>, bounded/unbounded queues, BoundedChannelFullMode, backpressure, TryWrite, WriteAsync, ReadAsync, ReadAllAsync, completion, cancellation, concurrent producers/consumers, BackgroundService workers, in-memory event queues, overload protection, and channel performance. DO NOT USE FOR: durable or cross-process messaging, distributed pub/sub, broker delivery guarantees, Rx.NET stream composition, task-parallel dataflow blocks, or simple thread-safe collections with no asynchronous producer-consumer requirement."
---

# .NET Channels

Use `System.Threading.Channels` for asynchronous, in-process FIFO producer-consumer pipelines. Define capacity, full behavior, concurrency promises, ownership, completion, cancellation, and failure handling before creating the channel.

## Workflow

1. Confirm Channels fit: one process, no required durability, and asynchronous producer-consumer handoff.
2. Define the item contract, accepted loss policy, ordering scope, capacity, producer/consumer concurrency, and stop behavior.
3. Choose a bounded channel by default. Use unbounded only with a proven finite/controlled backlog.
4. Select `FullMode`: `Wait` for lossless work, a drop mode only for intentional, observable loss.
5. Give producers only `ChannelWriter<T>` and consumers only `ChannelReader<T>`; hide the mutable `Channel<T>` implementation.
6. Await `WriteAsync`/`ReadAllAsync` with cancellation. Complete the writer once all producers finish; let consumers drain.
7. In hosted apps, use a singleton queue and a `BackgroundService`; create a DI scope per item or batch when consumers use scoped services.
8. Test full capacity, cancellation, completion, producer failure, consumer failure, concurrent ownership, and shutdown behavior.

## API Choice

| Need | API |
| --- | --- |
| Fast-path write/read without waiting | `TryWrite` / `TryRead` |
| Wait for capacity/data | `WriteAsync` / `ReadAsync` |
| Process a finite completed stream | `await foreach` over `ReadAllAsync` |
| Drain currently available items | `WaitToReadAsync` plus `TryRead` loop |
| Stop future writes | `TryComplete` or `Complete` |
| Observe completion/fault | `ChannelReader.Completion` |

## Invariants

- A channel is process-local and memory-resident; it is not a durable queue or distributed broker.
- `WriteAsync` can wait under `BoundedChannelFullMode.Wait`; do not fire-and-forget it.
- `Complete` ends writing, not reading. Readers drain buffered items before completion.
- With multiple producers, complete only after every producer has stopped writing.
- A drop mode silently loses data unless the channel's item-dropped callback or application telemetry records it.
- `SingleReader` and `SingleWriter` are performance promises. Set them to `true` only when the ownership is guaranteed.
- `AllowSynchronousContinuations` can execute a waiting continuation inline. Leave it `false` unless the reentrancy and latency effects are understood.

## Reference Files

| File | Load When |
| --- | --- |
| [references/contracts-and-api.md](references/contracts-and-api.md) | Choosing channel shapes, writer/reader APIs, completion, cancellation, or error behavior |
| [references/bounded-capacity-and-backpressure.md](references/bounded-capacity-and-backpressure.md) | Sizing a queue, selecting full modes, controlling overload, or handling dropped work |
| [references/concurrency-and-consumption.md](references/concurrency-and-consumption.md) | Using single/multiple producers or consumers, ordering, parallel work, draining, or batching |
| [references/hosted-services-and-di.md](references/hosted-services-and-di.md) | Building ASP.NET Core/background-service queues, scopes, graceful shutdown, or observability |
| [references/in-memory-messaging-boundaries.md](references/in-memory-messaging-boundaries.md) | Building an internal event bus or deciding whether a durable broker/outbox is required |
| [references/testing-and-review.md](references/testing-and-review.md) | Testing channel behavior or reviewing lifetime, backpressure, concurrency, and reliability |
| [references/sources.md](references/sources.md) | Rechecking API behavior and evaluating official or practitioner examples |
