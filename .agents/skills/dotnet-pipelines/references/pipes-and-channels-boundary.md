# Pipes And Channels Boundary

## Different Jobs

| Concern | Prefer |
| --- | --- |
| Buffered transport bytes, framing, partial messages | `System.IO.Pipelines` |
| Independently owned work items, bounded worker concurrency | `System.Threading.Channels` |
| Fan-out or durable/cross-process delivery | A dedicated messaging design or broker |

Parse on the pipe reader, create an independently owned message, and enqueue that message to a bounded channel. The channel consumers must never receive a `ReadOnlySequence<byte>` or a view over pipe-owned memory.

```csharp
while (TryReadFrame(ref buffer, out ReadOnlySequence<byte> frame))
{
    WorkItem item = DecodeOrCopy(frame);
    await workWriter.WriteAsync(item, cancellationToken);
}
```

Backpressure must be explicit at both stages:

- `PipeWriter.FlushAsync` limits retained transport bytes through pipe thresholds.
- A bounded channel limits parsed-but-unprocessed work items and applies the selected overload policy.
- Awaiting a bounded channel write inside the pipe reader retains its current buffer. Keep item ownership small, capacity bounded, and latency understood; alternatively decouple work admission at a deliberate ownership boundary.

## Avoid These Errors

- Do not fire-and-forget channel writes from the pipe reader; this loses failures and moves the queue into scheduled tasks.
- Do not dispatch pipe-backed payloads to background workers after `AdvanceTo`; copy/decode first.
- Do not use an unbounded channel to hide a downstream bottleneck. Choose a capacity and a documented full policy.
- Do not assume multiple channel consumers preserve global frame-processing order.

## Sources

- [System.IO.Pipelines](https://learn.microsoft.com/dotnet/standard/io/pipelines)
- [System.Threading.Channels](https://learn.microsoft.com/dotnet/core/extensions/channels)
- [Pipelines and Channels performance example](https://dev.to/joni2nja/use-system-io-pipelines-and-system-threading-channels-apis-to-boost-performance-2nj5)
