# Stationary

`Stationary.Ftms` is a `net11.0` library of high-performance, transport-neutral codecs for Bluetooth Fitness Machine Service (FTMS) payloads. The solution also contains TUnit tests and BenchmarkDotNet measurements.

## Commands

- Restore native workloads first: `dotnet workload restore src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj --version 11.0.100`. Use Xcode 27.2; check [native release prerequisites](RELEASING.md#current-release-gate) for upstream package availability.
- Restore packages: `dotnet restore Stationary.slnx`
- Build the solution: `dotnet build Stationary.slnx --no-restore`
- Apply formatting and analyzer fixes: `dotnet format Stationary.slnx --no-restore`
- Verify formatting and analyzer fixes: `dotnet format Stationary.slnx --verify-no-changes --no-restore`
- Run all tests: `dotnet test --solution Stationary.slnx --no-restore`
- Validate the static download site: `python3 -m unittest discover -s scripts -p 'test_*.py'`, then `python3 scripts/build_site.py`.
- Prepare a website DMG on a supported macOS toolchain: `bash scripts/publish-macos.sh <major.minor.patch> <build>`. See [release gates](RELEASING.md); never bypass Xcode validation for distribution.
- DMG presentation uses pinned build-only Python tools in `scripts/requirements-dmg.txt`, installed into a temporary venv by packaging. Regenerate its background with `swift scripts/render-dmg-background.swift scripts/dmg-background.png`; never add Finder extension-hiding metadata to the signed app.
- Linux CI (excludes the Mac Catalyst dashboard): `dotnet restore Stationary.CI.slnf`, `dotnet build Stationary.CI.slnf --no-restore`, then `dotnet test --solution Stationary.CI.slnf --no-build`. Keep the filter updated when adding portable projects.
- Build benchmarks: `dotnet build benchmarks/Stationary.Ftms.Benchmarks/Stationary.Ftms.Benchmarks.csproj -c Release --no-restore`
- Run benchmarks: `dotnet run --project benchmarks/Stationary.Ftms.Benchmarks/Stationary.Ftms.Benchmarks.csproj -c Release --no-build -- --filter '*FtmsCodecBenchmarks*' --join`
- Recompile dashboard shaders after editing them (requires `glslangValidator`, e.g. `brew install glslang`): in `src/Stationary.Ftms.Dashboard/Shaders`, run `glslangValidator -V <shader> -o <shader>.spv` for each edited shader (`scene.vert`, `scene.frag`, `post.vert`, `bloom_down.frag`, `bloom_up.frag`, `composite.frag`)

## Conventions

- Target `net11.0`; nullable reference types, analyzers, code-style enforcement, deterministic builds, and warnings-as-errors are configured in `Directory.Build.props`.
- Do not enable runtime-native async (`<Features>runtime-async=on</Features>`). The libraries ship inside the Mac Catalyst dashboard, and the .NET 11 Mac Catalyst framework is not compiled with runtime async.
- Serialize persisted dashboard data with source-generated `JsonSerializerContext` types, not reflection-based `JsonSerializer` overloads.
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
- Keep website source in `site/`, separate from OKF bundles. Website binaries are ad-hoc signed, not Apple-notarized; do not add paid provisioning, remove quarantine, or disable system security. Require owner approval and browser-download/hardware tests before publishing.
- Update these instructions with any change to build, test, or benchmark tooling.
