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

### Telemetry

- Indoor bikes often report speed from a fixed cadence-to-wheel ratio (the test bike reports 0.358 km/h per rpm at any power). When an indoor bike reports power, the dashboard replaces its speed with the flat-road, no-wind speed that power would sustain: 83 kg rider and bike, rolling resistance 0.004, drag area 0.32 m², air density 1.225 kg/m³, and 97.6% drivetrain efficiency. For example, 100 W gives about 26 km/h and 200 W about 34 km/h. Display, charts, and recorded sessions use this speed. Session distance for power-reporting indoor bikes is integrated from consecutive corrected-speed samples; gaps over five seconds and paused time are excluded, and the bike's distance counter is ignored. Other machines continue to use their reported distance counter.

### Structured Training

- Structured workouts send each segment's FTMS target-power command to the bike. In ERG mode, the bike controls resistance; the rider follows the segment's cadence target rather than trying to force measured power to match the target.
- Power target increases ramp in 5-second steps over up to 15 seconds (a quarter of shorter segments); budget trainers take 3-6 seconds to respond, so faster steps are skipped. Decreases apply at once. The first segment starts from half its target. The FTP ramp test applies its steps directly.
- Training holds the bike's lowest supported power target until cadence reaches 45 rpm. Only then does the first target ramp begin and the interval timer start, so the trainer does not load resistance against a stopped flywheel.
- If cadence stays below 35 rpm for 1.5 seconds during a workout, the target drops to the lowest supported power. As soon as cadence reaches 45 rpm, it ramps back to the interval target, starting from the higher of the rider's current power and the bike's estimated minimum at that cadence. Known limit: after an abrupt stop, the test bike's own controller kept resistance at about 3-3.6 W/rpm (roughly twice the target) for the first 3-5 seconds of the restart. Across 26 restarts no power command changed this, whether 0-130 W holds, repeated holds, or FTMS Pause and Start, while spinning up from idle at session start stayed near 1.4 W/rpm. With power-only control, avoiding stalls is the remedy. This guard prevents the ERG "spiral of death" and is disabled during the FTP ramp test.
- Every workout opens with a broad warm-up cadence range (60-100 rpm as authored), then a 2-minute cadence lift (75-95 rpm as authored), so riders approach the work range before the first scored interval. These opening segments do not count toward adherence.
- The cadence tile shows "Pedal faster" or "Pedal slower" while live cadence is outside the active range.
- The Power vs Cadence map shades the active cadence range as a band across every power and marks the ERG power target with a dashed line. The dot turns green whenever cadence is in range, because ERG trainers often settle a few percent away from the target and the rider cannot change that. While a target ramps and for 10 seconds afterward, the ride panel reminds the rider to hold cadence steady while the trainer adjusts resistance; when the cadence range changes, the window is 15 seconds and the panel asks the rider to move cadence smoothly into the new range. The segment cue appears afterward. Workout adherence measures time in the prescribed cadence range after the ramp and this settling period, excluding telemetry gaps longer than five seconds.
- Workout cadence ranges are authored for a 90 rpm rider. Settings accepts a preferred cadence from 40 to 110 rpm, and every range scales proportionally to it; for example, 85-95 rpm becomes 47-53 rpm at 50 rpm. A scaled range never starts below 45 rpm, the stall guard's pedalling threshold; lower ranges shift up and keep their width. A saved legacy cadence offset is migrated to 90 plus the offset.
- At the end of the opening warm-up, the median warm-up cadence refines the preferred cadence by up to 10 rpm for the rest of that ride, starting with the cadence lift. The ride panel reports the measured warm-up cadence and the tuned cadence when the limit applies; the saved setting does not change.
- During a workout, the Power vs Cadence map keeps fixed axes that span the whole workout. Cadence runs vertically (the rider moves the dot up or down) and power horizontally (the bike holds it in ERG mode, so the dashed target line only shifts sideways when the interval power changes). Samples outside the axes are pinned to the edge.
- The Power vs Cadence map has a light background with light-grey stars behind the grid. Stars are points 10-120 m ahead, seen through a 60° horizontal field of view that moves forward at the corrected bike speed, so they drift with real-world perspective: distant points barely move and nearby ones sweep outward. A star draws a streak only when its projected travel over 0.1 s exceeds 6% of the half-width, so easy riding around 12 km/h shows points only and streaks appear on hard efforts. Motion stops at zero speed, during pauses, and when telemetry is stale; rendering also stops when the page is hidden or the app is suspended. The cadence band, dashed power target, and rider marker stay in the foreground.
- Many bikes cannot go below a minimum resistance level, so at higher cadences low targets are unreachable. During a workout the dashboard learns this floor in watts per rpm. It starts once power has stayed more than 10% above a settled target for 10 seconds at 70 rpm or more. When a target is below the learned floor at the current cadence, the ride panel says power will read high, suggests the lowest cadence in the range that gets closer, and logs a `Training` event.
- Recorded sessions log `Training` events: the workout and preferred cadence, each interval's power and cadence targets, every target sent with the bike's response and round-trip time, pedalling detection, stalls and recoveries, every Fitness Machine Status notification from the bike (for example `TargetPowerChanged`), changes in the bike-reported resistance level, warm-up cadence tuning, and how the workout ended. The CSV export interleaves events with telemetry by timestamp. Charts do not draw `Training` events.
- The last plan, "Debug: Power vs Cadence", holds four sessions of under 10 minutes for checking how a bike follows ERG targets. Each opens with a 90-second calibration spin-up and ends with a 1-minute cool down. Power Steps changes power every 45 seconds in a fixed cadence band. Cadence Sweep holds power while the cadence band moves every minute. Big Jumps alternates 45% and 85% FTP every 30 seconds. Stall and Recover asks the rider to stop twice to exercise the stall guard. The sessions also appear at the end of the workout library and stay outside the validated training catalog and its plans.
- Workouts use ERG's power-torque trade-off: at the same power target, a lower cadence means more resistance (torque) and a higher cadence means less. Between scored intervals, either power or the cadence range changes, never both. Recovery adds quick-feet spin-ups at the same easy power. Endurance adds seated-climb (lower cadence) and high-cadence sections at Zone 2 power. Tempo and sweet spot add low-cadence torque blocks. Threshold keeps over-unders at a steady cadence and closes each set with a seated climb. VO2 efforts use a high cadence to limit torque. Cadence skills run ladders, pyramids, and torque-spin contrasts at fixed power. Scored work with ranges starting below 75 rpm (as authored) stays at or below 95% FTP to avoid grinding into a stall.
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
