# Testing, Performance, And Review

## Test Matrix

| Scenario | Assert |
| --- | --- |
| Frame split across reads | Reader retains the suffix and emits one correct frame |
| Multiple frames in one read | Reader emits each frame and advances past all consumed bytes |
| Final incomplete data | Protocol reports the chosen invalid/truncation outcome |
| Empty completion | Reader completes without a spin or leaked buffer |
| Oversized header/frame | Reader rejects it before unbounded buffering/allocation |
| Read/flush cancellation | Loop exits according to the ownership contract and completes safely |
| Consumer completes early | Writer observes `FlushResult.IsCompleted` and stops |
| Deferred handling | Payload remains correct after `AdvanceTo` because it was copied/decoded |

Use controlled in-memory pipes and deliberately segmented writes. Avoid timing-based tests; await a known read/flush boundary or use a test-controlled synchronization point.

## Review Checklist

1. Is a pipe justified by measured throughput, allocation, framing, or an existing pipe API?
2. Does every `ReadAsync` call receive one `AdvanceTo` in `finally`?
3. Do `consumed` and `examined` describe the parser's actual progress?
4. Can any pipe-owned buffer escape past `AdvanceTo`?
5. Are maximum header/frame sizes and invalid terminal data behavior defined?
6. Is every `FlushAsync` awaited and are `IsCompleted`/`IsCanceled` results handled?
7. Are reader/writer completion owners unambiguous and exceptions propagated?
8. Are calls to `ReadAsync` and `FlushAsync` serialized per pipe end?
9. If Channels follow parsing, are work items owned, bounded, and ordering-safe?

## Measure Before Tuning

- Compare end-to-end throughput, allocation rate, retained bytes, latency, and CPU under representative frame sizes and fragmentation.
- Benchmark alternative framing/copying strategies with realistic I/O and concurrency. Do not infer a general speedup from another workload's benchmark.
- Investigate sustained retained buffers, pending flushes, read loops that wake without new data, and growing incomplete frames.

## Sources

- [System.IO.Pipelines: common PipeReader problems](https://learn.microsoft.com/dotnet/standard/io/pipelines#pipereader-common-problems)
- [Pipelines and Channels performance example](https://dev.to/joni2nja/use-system-io-pipelines-and-system-threading-channels-apis-to-boost-performance-2nj5)
