# Hosted Services And Dependency Injection

## Queue Shape

- Register the queue owner as a singleton. Expose an application-specific enqueue interface rather than raw channel internals at public boundaries.
- Inject the queue's reader into one or more `BackgroundService` consumers. Start each consumer through `AddHostedService`.
- A hosted service is a singleton. Use `IServiceScopeFactory.CreateScope()` or `CreateAsyncScope()` per item or batch when resolving scoped dependencies such as database contexts.
- Pass the host's `stoppingToken` to `ReadAllAsync` and all processing operations. Define whether shutdown drains existing work or stops immediately.

## Consumer Pattern

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    await foreach (WorkItem item in reader.ReadAllAsync(stoppingToken))
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IWorkHandler handler = scope.ServiceProvider.GetRequiredService<IWorkHandler>();
        await handler.HandleAsync(item, stoppingToken);
    }
}
```

- Keep retries bounded and cancellation-aware. Do not retry an item forever inside one worker.
- Catch expected per-item failures at the item boundary, log structured context without sensitive data, and send failures to a deliberate retry/dead-letter/persistence path.
- Let unexpected infrastructure failures end the service when fail-fast supervision is appropriate; do not silently swallow them.

## Shutdown

- Stop admitting new work before completion. Call `TryComplete` from the component that owns producer shutdown.
- The default host shutdown token may cancel before the channel drains. Configure a suitable shutdown timeout and make the drain policy explicit.
- Do not use a process-local channel for work that cannot be lost during restart. Persist it transactionally or use a durable broker/outbox.

## Sources

- [Worker services](https://learn.microsoft.com/dotnet/core/extensions/workers)
- [Dependency injection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)
- [Channels](https://learn.microsoft.com/dotnet/core/extensions/channels)
