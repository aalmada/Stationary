# Stationary

Stationary is a .NET 11 toolkit for Bluetooth Fitness Machine Service (FTMS) equipment. It provides high-performance, transport-neutral codecs together with BLE connectivity, serialized Control Point coordination, telemetry presentation, heart-rate support, and a .NET MAUI dashboard for multiplatform app delivery. The dashboard uses ReactiveUI and Reactive Extensions for reactive MVVM and real-time event processing.

![Stationary FTMS dashboard Session view with live telemetry and heart-rate analytics](Screenshot%202026-09-11%20at%2019.50.29.png)

## Projects

| Project | Purpose |
| --- | --- |
| `Stationary.Ftms` | Allocation-conscious FTMS payload codecs and validation. |
| `Stationary.Ble` | BLE transport integration. |
| `Stationary.Ftms.Client` | Serialized FTMS Control Point operations and latest-only telemetry delivery. |
| `Stationary.Ftms.Dashboard.Core` | Telemetry history and dashboard-domain models. |
| `Stationary.Ftms.Dashboard.ViewModels` | Portable ReactiveUI presentation state, generated commands, and platform-service contracts. |
| `Stationary.HeartRate` | Heart-rate telemetry and cycling-LTHR analytics. |
| `Stationary.Ftms.Dashboard` | .NET MAUI dashboard for compatible FTMS equipment. |

## Reactive Architecture

The dashboard is a fully reactive .NET MAUI application built with ReactiveUI's reactive MVVM model and Reactive Extensions (Rx). `ReactiveCommand` represents user operations, while observable state derives UI feedback and availability from the underlying streams.

Reactive pipelines coordinate real-time BLE connection lifecycle, FTMS telemetry, heart-rate measurements, Fitness Machine Status updates, Control Point responses, and dashboard commands. They merge, sample, serialize, schedule, and dispose these concurrent streams so the UI stays responsive while displaying the latest available data.

View models compile in a separate `net11.0` assembly before the MAUI app. ReactiveUI generates their properties and commands there; MAUI's XAML source generator then resolves the completed members from the referenced assembly. Roslyn generators cannot consume each other's output within one compilation, so keep this project boundary rather than relying on generator ordering or reflection fallbacks.

The view-model library references the portable `Microsoft.Maui.Graphics` package for presentation colors, but not MAUI Controls, Essentials, or Plugin.BLE. Discovery/session interfaces and `IDashboardPlatform` belong to this library. The app supplies BLE implementations and `MauiDashboardPlatform` for preferences, cache/app-data paths, and main-thread dispatch through constructor injection.

### Framework Versions

- .NET MAUI `11.0.0-rc.2.26505.5`, the latest published .NET 11 prerelease as of October 28, 2026. The dashboard remains on the .NET 11 preview line rather than downgrading to MAUI 10. It does not reference the legacy `Microsoft.Maui.Controls.Compatibility` package.
- ReactiveUI `20.4.1`, upgraded from `19.6.12` and pinned to the newest version verified to pass the normal trimmed Mac Catalyst build with this SDK.
- Reactive Extensions (`System.Reactive`) `7.0.0`, upgraded from `6.0.0`. Rx.NET 7 separates Windows-specific UI support into platform packages; this Mac Catalyst application needs no additional Rx platform package.
- ReactiveUI.SourceGenerators `2.6.1`, the newest release that supports ReactiveUI 20; 3.x and 4.x require ReactiveUI `23.2.28` or later.

The dashboard references only core ReactiveUI; `ReactiveUI.Maui` is not needed because no ReactiveUI views or MAUI-specific registrations are used, and it would pull in the legacy Compatibility package. `MauiProgram` calls `PlatformRegistrationManager.SetRegistrationNamespaces(RegistrationNamespace.None)` before anything touches `RxApp`, so ReactiveUI does not probe for optional platform assemblies during static initialization. The core package still supplies the Mac Catalyst main-thread scheduler.

ReactiveUI `26.0.1` is the latest published release, but it is not currently compatible with this project's normal build. Tested stable releases from the 21.x through 24.x lines, plus `25.1.1`, `26.0.0`, and `26.0.1`, fail trimming with `IL2037`: their preservation metadata requests constructors on static types such as `DependencyResolverMixins`. The 25.x .NET 11 assemblies also have a Mono runtime-async compatibility issue described in the ReactiveUI 26 release notes. Do not adopt the newer binding engine, builder APIs, or `RxSchedulers` migration until a compatible release is available. Trimming and warnings-as-errors remain enabled; no diagnostic suppressions are required for `20.4.1`.

See the [ReactiveUI 20.4.1 release notes](https://github.com/reactiveui/ReactiveUI/releases/tag/20.4.1), [ReactiveUI 26 compatibility notes](https://github.com/reactiveui/ReactiveUI/releases/tag/26.0.0), and [Rx.NET 7 release history](https://github.com/dotnet/reactive/blob/main/Rx.NET/Documentation/ReleaseHistory/Rx.v7.md).

### Dashboard Conventions

- XAML files use MAUI 11 implicit namespaces and compiled bindings (`x:DataType`) everywhere, including `DataTemplate`s. Bindings to a parent view model from a template use `RelativeSource AncestorType` instead of `x:Reference`.
- View-model state uses source-generated partial properties: `[Reactive]` for settable state and `[ObservableAsProperty]` for derived state. Dependent notifications are raised explicitly, because `[Reactive]` `alsoNotify` in `2.6.1` raises them unconditionally and twice.
- Commands use `[ReactiveCommand]` methods, including commands bound in XAML, with `CanExecute` naming an `IObservable<bool>` field initialized before the command is accessed. Keep Control Point operations behind the shared serialization gate, and retain `IsExecuting`, `ThrownExceptions`, and disposal handling.
- XAML namespace declarations for view models and discovery models include `assembly=Stationary.Ftms.Dashboard.ViewModels`. Keep views, converters, and platform implementations in the MAUI app; do not move generated view models back into its compilation.

### Structured Training

- Structured workouts send each segment's FTMS target-power command to the bike. In ERG mode, the bike controls resistance; the rider follows the segment's cadence target rather than trying to force measured power to match the target.
- The Power vs Cadence map shades the active power/cadence target zone. For the first 10 seconds after each target change, the ride panel reminds the rider to hold cadence steady while the trainer adjusts resistance; the segment cue appears afterward. Workout adherence measures time in the prescribed cadence range after this settling period, excluding telemetry gaps longer than five seconds.
- Settings can shift all workout cadence ranges by -15 to +15 rpm for the rider's preferred cadence. Zero keeps the authored ranges.
- Anaerobic workouts are controlled ERG efforts, not all-out sprints. Each effort has a cadence-build segment; recovery before subsequent efforts is at least 2:1. The dashboard does not currently offer resistance-mode sprint workouts.
- Workout targets are generic FTP-relative prescriptions. Set FTP accurately and adjust or stop an interval if the effort is unsuitable.

## Prerequisites

- .NET SDK `11.0.100-rc.3.26522.148`, as pinned in [global.json](global.json). If installed under `~/.dotnet`, put that directory first on `PATH`.
- The .NET MAUI workload for the platform being built. On macOS, install the Mac Catalyst workload for the currently configured dashboard target:

```bash
dotnet workload install maui-maccatalyst
```

## Build And Test

Restore packages once, then use the no-restore commands for normal iteration:

```bash
dotnet restore Stationary.slnx
dotnet build Stationary.slnx --no-restore -p:ValidateXcodeVersion=false
dotnet test --solution Stationary.slnx --no-restore
dotnet format Stationary.slnx --verify-no-changes --no-restore
```

The portable view models and their regression tests are included in Linux CI:

```bash
dotnet restore Stationary.CI.slnf
dotnet build Stationary.CI.slnf --no-restore
dotnet test --solution Stationary.CI.slnf --no-build
```

## Run The Mac Catalyst Dashboard

On Apple silicon:

```bash
dotnet build src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj \
  -t:Run \
  -f net11.0-maccatalyst \
  -r maccatalyst-arm64 \
  --no-restore \
  -p:ValidateXcodeVersion=false
```

Use `maccatalyst-x64` on an Intel Mac.

The dashboard uses .NET MAUI as its multiplatform application foundation. The checked-in project currently targets Mac Catalyst; add the required MAUI target frameworks and workloads before building additional platform apps.

## Benchmarks

```bash
dotnet build benchmarks/Stationary.Ftms.Benchmarks/Stationary.Ftms.Benchmarks.csproj \
  -c Release \
  --no-restore

dotnet run --project benchmarks/Stationary.Ftms.Benchmarks/Stationary.Ftms.Benchmarks.csproj \
  -c Release \
  --no-build \
  -- --filter '*FtmsCodecBenchmarks*' --join
```

## Documentation

- [FTMS v1.0.1 reference](docs/ftms/index.md)
- [Documentation index](docs/index.md)
