# Testing And Review

## Test Matrix

| Scenario | Assert |
| --- | --- |
| Empty, completed channel | `ReadAllAsync` ends without items |
| Bounded `Wait` channel is full | A writer waits until a read frees capacity |
| Drop policy is full | Correct item is discarded and loss is observed |
| Producer cancellation | Pending write cancels without completing the writer |
| Consumer cancellation | Enumeration stops without corrupting queue ownership |
| Multiple producers | Completion occurs only after all producers finish |
| Multiple consumers | Every item is processed once; order is not assumed |
| Producer completion fault | Consumers receive/drain as designed and supervisor observes fault |
| Hosted shutdown | Admission, drain/cancel policy, scope disposal, and timeout are correct |

Use test-controlled tasks, barriers, `TaskCompletionSource`, and short explicit timeouts to make contention deterministic. Do not use `Task.Delay` as synchronization or call blocking waits such as `.Result`/`.Wait()`.

## Review Checklist

1. Does the process-local, non-durable contract match the business requirement?
2. Is capacity bounded, or is unbounded growth justified and monitored?
3. Does full-mode behavior match the required loss/backpressure policy?
4. Are writer, reader, completion, and cancellation owners explicit?
5. Are `SingleWriter` and `SingleReader` accurate promises?
6. Is each item outcome explicit: success, retry, discard, durable failure record, or pipeline stop?
7. Are hosted consumers scope-safe, cancellation-aware, observable, and shutdown-tested?
8. Does concurrent consumption preserve all required ordering constraints?

## Diagnostics

- Record item type/category, enqueue attempt outcome, producer wait, consumer duration, retry count, failure class, drop count, and queue age where measurable.
- Avoid logging raw payloads or sensitive data. Correlate an opaque operation ID through producer and consumer logs.
- Alert on sustained producer waits, repeated drops, growing item age, crash-looping consumers, and drain timeout.

## Sources

- [Channels](https://learn.microsoft.com/dotnet/core/extensions/channels)
- [Building high-performance .NET apps with C# Channels](https://antondevtips.com/blog/building-high-performance-dotnet-apps-with-csharp-channels)
