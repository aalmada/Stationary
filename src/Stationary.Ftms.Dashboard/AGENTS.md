# Stationary.Ftms.Dashboard

Mac Catalyst MAUI dashboard for connecting to FTMS equipment and presenting telemetry and supported controls.

## Commands

- Build: `dotnet build src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj --no-restore`
- Run on Apple silicon: `dotnet build src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj -t:Run -f net10.0-maccatalyst -r maccatalyst-arm64 --no-restore`

## Reactive UI

- Use the installed System.Reactive and ReactiveUI packages for every asynchronous UI workflow.
- Adapt service events with observables at the boundary; compose, schedule, and dispose subscriptions through ReactiveUI lifetimes.
- Model writable state with `ReactiveObject`; derive display and availability state with observable pipelines and `ToProperty`.
- Use `ReactiveCommand` for user operations. Derive `CanExecute` from observable state, bind `IsExecuting` to local progress feedback, and handle `ThrownExceptions` visibly.
- Keep UI state declarative: bind loading, connected, control, telemetry, and error states. Do not use imperative busy counters or control event handlers for application logic.
- Compose concurrent device, telemetry, and command streams into responsive status and availability state. Deduplicate in-flight commands, serialize conflicting operations, and transition persistent UI only after confirmed device outcomes.

## FTMS UX

- Keep all controls protocol-backed and functional. Do not show an actionable control unless the device advertises support and its required range or parameter constraints are known.
- Serialize Control Point requests, await their matching indications, and present pending, accepted, rejected, timed-out, and disconnected states.
- Preserve latest-only telemetry delivery and marshal bound collection changes to the MAUI main thread.
