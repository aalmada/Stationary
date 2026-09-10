---
name: dotnet-pipelines
description: "Design, implement, debug, test, and review high-throughput byte-stream processing with System.IO.Pipelines. USE FOR: Pipe, PipeReader, PipeWriter, ReadResult, FlushResult, ReadOnlySequence, SequenceReader, buffer lifecycle, AdvanceTo, GetMemory, Advance, FlushAsync, stream and socket adapters, framing protocols, incremental parsing, backpressure, cancellation, and Pipes-to-Channels handoff. DO NOT USE FOR: item-level producer-consumer queues, durable/distributed messaging, ordinary small file I/O with no measured bottleneck, generic Stream APIs with no buffer-management concern, or ASP.NET middleware pipelines unrelated to System.IO.Pipelines."
---

# .NET Pipelines

Use `System.IO.Pipelines` for high-throughput asynchronous byte streams where buffer ownership, partial reads, and copying cost matter. Prefer `Stream` APIs until profiling identifies a pipeline-shaped bottleneck or a component already exposes a pipe.

## Workflow

1. Confirm the workload is a byte stream with partial messages, large data, or a measured allocation/throughput need.
2. Define framing, maximum message size, invalid-data policy, cancellation, completion ownership, and whether payload bytes must outlive a read iteration.
3. Create a `Pipe` for an in-memory producer/consumer pair, or adapt an existing `Stream` with `PipeReader.Create` or `PipeWriter.Create`.
4. Writer: obtain memory, write bytes, call `Advance`, then await `FlushAsync`; stop when the flush result is completed or canceled.
5. Reader: await `ReadAsync`, parse only complete frames, and always call `AdvanceTo(consumed, examined)` in `finally`.
6. Copy or decode a parsed payload before `AdvanceTo` if it outlives the current buffer; pipe memory becomes invalid after advancing.
7. Complete the reader/writer exactly once from the component that owns its full loop; propagate faults through `CompleteAsync(exception)`.
8. Use a bounded `Channel<T>` only after parsing creates independently owned work items; do not enqueue pipe-backed sequences.
9. Test fragmentation, coalesced frames, incomplete final data, cancellation, limits, completion, and buffer lifetime.

## API Choice

| Need | API |
| --- | --- |
| Create producer/consumer byte pipe | `new Pipe(options)` |
| Adapt a stream as a reader/writer | `PipeReader.Create` / `PipeWriter.Create` |
| Reserve and commit write memory | `GetMemory` or `GetSpan`, then `Advance` |
| Publish written bytes/backpressure | `FlushAsync` |
| Read a byte sequence | `ReadAsync` returning `ReadResult` |
| Parse segmented data | `ReadOnlySequence<byte>` and `SequenceReader<byte>` |
| Release/read-more signaling | `AdvanceTo(consumed, examined)` |
| Non-exceptional pending-operation stop | `CancelPendingRead` / `CancelPendingFlush` |
| End ownership and surface faults | `CompleteAsync(exception)` |

## Invariants

- There is exactly one outstanding read and one outstanding flush; do not call `ReadAsync` or `FlushAsync` concurrently on the same pipe end.
- Every `ReadAsync` result receives exactly one `AdvanceTo`, including error and cancellation paths.
- `consumed` releases bytes; `examined` tells the pipe where parsing stopped. Incorrect positions can cause replay, stalls, infinite buffering, OOM, or data loss.
- `ReadOnlySequence<byte>` and its spans are valid only until the reader advances. Copy/decode data before dispatching it elsewhere.
- A completed reader still exposes buffered bytes. Treat final incomplete frames as a protocol decision, usually invalid input.
- Set protocol-level size limits; an incomplete oversized frame can retain memory until it exhausts the process.

## Reference Files

| File | Load When |
| --- | --- |
| [references/reader-parsing-and-lifetime.md](references/reader-parsing-and-lifetime.md) | Implementing framing, `AdvanceTo`, `ReadOnlySequence`, `SequenceReader`, or parsing incomplete data |
| [references/writer-flow-control-and-completion.md](references/writer-flow-control-and-completion.md) | Writing bytes, flush backpressure, pipe options, cancellation, completion, or faults |
| [references/streams-sockets-and-ownership.md](references/streams-sockets-and-ownership.md) | Adapting streams/sockets, selecting ownership, or configuring leave-open/lifetime behavior |
| [references/pipes-and-channels-boundary.md](references/pipes-and-channels-boundary.md) | Combining parsing throughput with bounded concurrent item processing |
| [references/testing-performance-and-review.md](references/testing-performance-and-review.md) | Testing, profiling, diagnosing memory pressure, or reviewing pipeline correctness |
| [references/sources.md](references/sources.md) | Rechecking API details and evaluating official or community guidance |
