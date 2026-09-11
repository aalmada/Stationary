# Stationary

`Stationary.Ftms` is a `net10.0` library of high-performance, transport-neutral codecs for Bluetooth Fitness Machine Service (FTMS) payloads. The solution also contains TUnit tests and BenchmarkDotNet measurements.

## Commands

- Restore packages: `dotnet restore Stationary.slnx`
- Build the solution: `dotnet build Stationary.slnx --no-restore`
- Apply formatting and analyzer fixes: `dotnet format Stationary.slnx --no-restore`
- Verify formatting and analyzer fixes: `dotnet format Stationary.slnx --verify-no-changes --no-restore`
- Run all tests: `dotnet test --solution Stationary.slnx --no-restore`
- Build benchmarks: `dotnet build benchmarks/Stationary.Ftms.Benchmarks/Stationary.Ftms.Benchmarks.csproj -c Release --no-restore`
- Run benchmarks: `dotnet run --project benchmarks/Stationary.Ftms.Benchmarks/Stationary.Ftms.Benchmarks.csproj -c Release --no-build -- --filter '*FtmsCodecBenchmarks*' --join`

## Conventions

- Target `net10.0`; nullable reference types, analyzers, code-style enforcement, deterministic builds, and warnings-as-errors are configured in `Directory.Build.props`.
- Keep codec hot paths allocation-free and transport-neutral. Prefer `ReadOnlySpan<byte>`, `Span<byte>`, and `BinaryPrimitives` for FTMS wire data.
- Preserve little-endian encoding and the `FtmsValidationMode` strict/compatible contract.
- Use central package management in `Directory.Packages.props`; do not hand-edit `apm.lock.yaml`.
- Run `dotnet format` after C# changes and fix every reported Roslyn analyzer issue; do not suppress analyzer diagnostics to bypass a fix.
- Keep dashboard workflows functional end to end: show a control only when the connected device supports it, await the FTMS response, and present accepted, rejected, and unavailable states.
- For every dashboard UI state change, use ReactiveUI: route user input and settings through `ReactiveCommand`, service events through observable pipelines, and derive display and availability state with `ToProperty` and bindings. Do not mutate bound state from code-behind, direct two-way controls, or imperative event handlers; use `IsExecuting` and `ThrownExceptions` for command feedback.
- Keep client-side Control Point operations serialized and telemetry delivery latest-only; do not block BLE notification callbacks on dashboard consumption.

## Scope And Safety

- Do not add agent instructions inside `docs/ftms` or other OKF bundles.
- Do not add Bluetooth discovery, GATT connection management, or platform-specific transport behavior to the codec library.
- Do not commit credentials or connection strings. Request confirmation before destructive operations.
- Update these instructions with any change to build, test, or benchmark tooling.
