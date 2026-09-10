# Reader Parsing And Lifetime

## Reader Loop

Call `AdvanceTo` exactly once for every `ReadAsync`, in `finally`. Parse complete frames only; retain an incomplete suffix by setting `consumed` to its start.

```csharp
while (true)
{
    ReadResult result = await reader.ReadAsync(cancellationToken);
    ReadOnlySequence<byte> buffer = result.Buffer;

    try
    {
        while (TryReadFrame(ref buffer, out ReadOnlySequence<byte> frame))
        {
            await ProcessFrameAsync(frame, cancellationToken);
        }

        if (result.IsCompleted)
        {
            if (!buffer.IsEmpty)
            {
                throw new InvalidDataException("Incomplete frame at end of input.");
            }

            break;
        }
    }
    finally
    {
        reader.AdvanceTo(buffer.Start, buffer.End);
    }
}
```

This pattern consumes every complete frame and marks the remainder as examined. For a single-frame read, track separate `consumed` and `examined` positions; on no complete frame, use `buffer.Start` and `buffer.End` so the reader waits for more data rather than spinning.

## Position Semantics

| Position | Meaning |
| --- | --- |
| `consumed` | Bytes no longer needed; the pipe may reclaim them |
| `examined` | Furthest byte inspected while looking for another frame |
| `buffer.Start` | Nothing consumed from this read |
| `buffer.End` | All received bytes inspected |

- Advancing `consumed` past unprocessed input loses data.
- Repeatedly examining only the start can make `ReadAsync` return immediately and spin.
- Examining `buffer.End` while retaining data tells the pipe not to wake until more data arrives; use it only after actually inspecting all available data.
- Never break before the matching `AdvanceTo`; use `finally`.

## Sequence Lifetime

- A `ReadOnlySequence<byte>` can span multiple memory segments. Do not assume `FirstSpan` contains a whole frame.
- Use `SequenceReader<byte>` or sequence APIs for segmented delimiters/length prefixes.
- Copy or decode bytes before `AdvanceTo` when a handler, task, channel, or object keeps them beyond the iteration.
- Do not store pipe-provided `Memory<byte>`, `Span<byte>`, or `ReadOnlySequence<byte>` in a message model that survives an advance.

## Limits

Set a maximum frame/header length before waiting indefinitely for a delimiter or declared payload. Reject invalid lengths and incomplete terminal data according to the protocol; an unconsumed attacker-controlled frame can retain unbounded memory.

## Sources

- [System.IO.Pipelines: PipeReader](https://learn.microsoft.com/dotnet/standard/io/pipelines#pipereader)
- [ReadOnlySequence](https://learn.microsoft.com/dotnet/api/system.buffers.readonlysequence-1)
- [SequenceReader](https://learn.microsoft.com/dotnet/api/system.buffers.sequencereader-1)
