# Stationary

Stationary is a .NET 10 toolkit for Bluetooth Fitness Machine Service (FTMS) equipment. It provides high-performance, transport-neutral codecs together with BLE connectivity, serialized Control Point coordination, telemetry presentation, heart-rate support, and a .NET MAUI dashboard for multiplatform app delivery. The dashboard uses ReactiveUI and Reactive Extensions for reactive MVVM and real-time event processing.

![Stationary FTMS dashboard Session view with live telemetry and heart-rate analytics](Screenshot%202026-09-10%20at%2021.51.52.png)

## Projects

| Project | Purpose |
| --- | --- |
| `Stationary.Ftms` | Allocation-conscious FTMS payload codecs and validation. |
| `Stationary.Ble` | BLE transport integration. |
| `Stationary.Ftms.Client` | Serialized FTMS Control Point operations and latest-only telemetry delivery. |
| `Stationary.Ftms.Dashboard.Core` | Telemetry history and dashboard-domain models. |
| `Stationary.HeartRate` | Heart-rate telemetry and cycling-LTHR analytics. |
| `Stationary.Ftms.Dashboard` | .NET MAUI dashboard for compatible FTMS equipment. |

## Reactive Architecture

The dashboard is a fully reactive .NET MAUI application built with ReactiveUI's reactive MVVM model and Reactive Extensions (Rx). `ReactiveCommand` represents user operations, while observable state derives UI feedback and availability from the underlying streams.

Reactive pipelines coordinate real-time BLE connection lifecycle, FTMS telemetry, heart-rate measurements, Fitness Machine Status updates, Control Point responses, and dashboard commands. They merge, sample, serialize, schedule, and dispose these concurrent streams so the UI stays responsive while displaying the latest available data.

## Prerequisites

- .NET SDK `10.0.401`, as pinned in [global.json](global.json).
- The .NET MAUI workload for the platform being built. On macOS, install the Mac Catalyst workload for the currently configured dashboard target:

```bash
dotnet workload install maui-maccatalyst
```

## Build And Test

Restore packages once, then use the no-restore commands for normal iteration:

```bash
dotnet restore Stationary.slnx
dotnet build Stationary.slnx --no-restore
dotnet test --solution Stationary.slnx --no-restore
dotnet format Stationary.slnx --verify-no-changes --no-restore
```

## Run The Mac Catalyst Dashboard

On Apple silicon:

```bash
dotnet build src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj \
  -t:Run \
  -f net10.0-maccatalyst \
  -r maccatalyst-arm64 \
  --no-restore
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
