# In-Memory Messaging Boundaries

## Valid Uses

- Decouple modules inside one process.
- Queue bounded background work after the initiating request has decided its own result.
- Serialize non-thread-safe processing or limit expensive concurrent work.
- Coalesce best-effort state updates when the newest value matters more than history.

## Not A Durable Broker

| Requirement | Use Instead Of Only A Channel |
| --- | --- |
| Survive process restart/crash | Durable queue, transactional outbox, or persisted job store |
| Cross process/machine delivery | Message broker or transport-backed queue |
| At-least-once/exactly-once semantics | Durable transport plus idempotency and deduplication design |
| Independent subscriber fan-out | Topic/pub-sub transport or separate managed queues |
| Operations replay/audit | Persistent event log or queue |

Channels deliver in memory only. A successful `WriteAsync` means the item entered the current process's queue, not that a consumer completed it or that it will survive a failure.

## In-Memory Event Bus

- Keep the public contract at the domain/event abstraction, not `Channel<T>`.
- Use a bounded queue when producers are request paths. Decide whether overload awaits, rejects, coalesces, or drops each event category.
- Create a DI scope for each event handler dispatch if handlers use scoped services.
- Add idempotency, retries, error recording, and observability before using it for meaningful side effects.
- Replace the channel with a durable boundary before splitting the process or requiring recovery guarantees. Do not present an in-memory queue as reliable integration messaging.

## Sources

- [Lightweight in-memory message bus](https://milanjovanovic.tech/blog/lightweight-in-memory-message-bus-using-dotnet-channels)
- [Channels](https://learn.microsoft.com/dotnet/core/extensions/channels)
