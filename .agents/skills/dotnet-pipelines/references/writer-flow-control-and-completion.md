# Writer Flow Control And Completion

## Writer Loop

Reserve memory, write bytes, commit the written count, then flush. `FlushAsync` publishes bytes and applies the pipe's pause/resume thresholds.

```csharp
try
{
    while (true)
    {
        Memory<byte> destination = writer.GetMemory(minimumBufferSize);
        int bytesRead = await source.ReadAsync(destination, cancellationToken);
        if (bytesRead == 0)
        {
            break;
        }

        writer.Advance(bytesRead);
        FlushResult flush = await writer.FlushAsync(cancellationToken);
        if (flush.IsCompleted || flush.IsCanceled)
        {
            break;
        }
    }
}
finally
{
    await writer.CompleteAsync();
}
```

Do not call `Advance` with bytes not initialized by the producer. Do not retain writer-provided memory after a flush or completion.

## Flow Control

| Setting | Effect |
| --- | --- |
| `PauseWriterThreshold` | Buffered-byte level where `FlushAsync` starts waiting |
| `ResumeWriterThreshold` | Buffered-byte level where a blocked flush may continue |
| `MinimumSegmentSize` | Minimum rented buffer size for writer requests |
| `UseSynchronizationContext` | Whether continuations capture the current synchronization context |

- Tune thresholds only after measurement. They constrain buffered bytes but do not replace protocol message limits.
- Await every `FlushAsync`; ignoring its pending result defeats flow control and hides completion/cancellation.
- Keep pause thresholds comfortably above typical frames but bounded by a real memory budget.

## Completion And Cancellation

- The component owning the full write loop calls `CompleteAsync`, including an exception when it owns a terminal fault.
- Completing the writer tells the reader no more bytes will arrive; readers must still drain the existing buffer.
- `ReadAsync`/`FlushAsync` accept cancellation tokens. `CancelPendingRead` and `CancelPendingFlush` produce a result with `IsCanceled` instead of throwing and are useful for a controlled loop exit.
- Stop writing when `FlushResult.IsCompleted` is true: the reader no longer accepts data.

## Sources

- [System.IO.Pipelines: Pipe](https://learn.microsoft.com/dotnet/standard/io/pipelines#pipe)
- [PipeOptions](https://learn.microsoft.com/dotnet/api/system.io.pipelines.pipeoptions)
- [PipeWriter](https://learn.microsoft.com/dotnet/api/system.io.pipelines.pipewriter)
