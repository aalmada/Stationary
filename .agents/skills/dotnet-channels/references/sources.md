# Sources

## Source Precedence

1. The target framework's `System.Threading.Channels` reference assembly and source.
2. Current Microsoft Learn API and conceptual documentation.
3. Official .NET samples and test cases.
4. Practitioner articles as architecture examples, not API authority.

## Reference Links

| Source | Use |
| --- | --- |
| [Channels - .NET](https://learn.microsoft.com/dotnet/core/extensions/channels) | Primary contract, factory options, producer/consumer APIs, and concurrency patterns |
| [System.Threading.Channels API](https://learn.microsoft.com/dotnet/api/system.threading.channels) | Type signatures, exceptions, and target-framework availability |
| [Worker services](https://learn.microsoft.com/dotnet/core/extensions/workers) | `BackgroundService`, host lifecycle, and DI-hosted consumer patterns |
| [High-performance Channels article](https://antondevtips.com/blog/building-high-performance-dotnet-apps-with-csharp-channels) | Bounded worker queue and backpressure example |
| [In-memory message bus article](https://milanjovanovic.tech/blog/lightweight-in-memory-message-bus-using-dotnet-channels) | Modular-monolith event queue example and durability limitations |
| [Channels video](https://www.youtube.com/watch?v=gT06qvQLtJ0) | Supplementary conceptual walkthrough; verify claims against the preceding sources |

- `System.Threading.Channels` is part of the shared framework on .NET Core 3.0 and later; inspect the target framework before adding a package reference.
- Verify behavior against the installed target framework when relying on new overloads, item-dropped callbacks, performance claims, or implementation details.
