# Streams, Sockets, And Ownership

## Adapter Choice

| Source Or Sink | Use |
| --- | --- |
| Existing `Stream` input | `PipeReader.Create(stream, options)` |
| Existing `Stream` output | `PipeWriter.Create(stream, options)` |
| In-memory producer/consumer pair | `new Pipe(options)` |
| Socket receive loop | `PipeWriter.GetMemory`, receive into it, `Advance`, then `FlushAsync` |
| Socket send loop | `PipeReader.ReadAsync`, send sequence bytes, then `AdvanceTo` |

Pipe adapters centralize buffer management. Use `StreamPipeReaderOptions` or `StreamPipeWriterOptions` to define buffer sizes and whether completing the pipe leaves the underlying stream open.

## Ownership Rules

- Decide which component owns the stream/socket and which owns each pipe end before passing it across an API boundary.
- A method that owns the entire read/write loop also owns `CompleteAsync`. A helper that reads one frame must not complete a reader it did not create.
- Complete the pipe end before disposing its underlying transport, unless the transport lifetime contract requires an abort.
- Propagate a terminal transport exception through the corresponding `CompleteAsync(exception)` so the peer observes the cause.
- Do not use concurrent reads, concurrent flushes, or multiple owners on one pipe end.

## Protocol Framing

- Pipes carry arbitrary byte chunks, not messages. A transport read can split one frame or combine many frames.
- Parse fixed-size, delimiter, or length-prefixed frames incrementally. Validate headers and declared lengths before allocating/copying payloads.
- Process all available complete frames per read when possible; preserve the incomplete suffix through `AdvanceTo`.

## Sources

- [System.IO.Pipelines: streams](https://learn.microsoft.com/dotnet/standard/io/pipelines#streams)
- [PipeReader.Create](https://learn.microsoft.com/dotnet/api/system.io.pipelines.pipereader.create)
- [PipeWriter.Create](https://learn.microsoft.com/dotnet/api/system.io.pipelines.pipewriter.create)
