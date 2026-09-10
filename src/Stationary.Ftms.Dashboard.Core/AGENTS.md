# Dashboard Core Instructions

- Build with `dotnet build src/Stationary.Ftms.Dashboard.Core/Stationary.Ftms.Dashboard.Core.csproj --no-restore`.
- Keep this project UI- and transport-neutral; it owns immutable telemetry models, bounded delivery buffers, and presentation-safe history only.
- Preserve latest-only telemetry semantics: the bounded buffer must retain the newest snapshot under load and must never block a producer.
- Bound history by its configured time window and avoid unbounded collections or per-sample work unrelated to display state.
- Do not introduce MAUI, BLE, ReactiveUI, or platform-specific dependencies here.
