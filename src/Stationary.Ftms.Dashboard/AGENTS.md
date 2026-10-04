# Stationary.Ftms.Dashboard

Mac Catalyst MAUI dashboard for connecting to FTMS equipment and presenting telemetry and supported controls.

## Commands

- Build: `dotnet build src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj --no-restore`
- Run on Apple silicon: `dotnet build src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj -t:Run -f net10.0-maccatalyst -r maccatalyst-arm64 --no-restore -p:ValidateXcodeVersion=false`

## Mac Catalyst Training Page

- Keep the Training `ShellContent` lazy. Eager XAML materialization previously crashed the application during startup.
- Do not reintroduce the `Picker` removed from `TrainingPage.xaml`; it previously crashed navigation to Training. Replace it only with a separately tested selector.
- Apply the shared `ActionButton` style to every Training action, including ramp-test and currently unavailable actions. It is the established Sensors-page pattern and provides the correct enabled and disabled presentation.

## Mac Catalyst Button Rendering

- Apply `ActionButton` or `SecondaryActionButton` to every dashboard button. Both styles use opaque command fills (`Accent` and `SecondaryAccent`); do not create page-local button fills or low-contrast surface buttons.
- Preserve both `ButtonHandler` mappings in `MauiProgram` for `IView.Background` and `IView.IsEnabled`.
- In the shared handler, set `UIButtonConfiguration.BaseBackgroundColor`, copy the value-style `Background` configuration, set its `BackgroundColor`, and assign it back. Mutating `configuration.Background.BackgroundColor` without reassignment does not reliably update native fills.
- Verify new button work in the running Mac Catalyst app. Check at least one `ActionButton`, one `SecondaryActionButton`, and a disabled button; all must render a full opaque fill.

## Reactive UI

- Use the installed System.Reactive and ReactiveUI packages for every dashboard state change: route user input and settings through `ReactiveCommand`, service events through observable pipelines, and derived state through bindings and `ToProperty`. Do not mutate bound state from code-behind, direct two-way controls, or imperative event handlers.
- Adapt service events with observables at the boundary; compose, schedule, and dispose subscriptions through ReactiveUI lifetimes.
- Model writable state with `ReactiveObject`; derive display and availability state with observable pipelines and `ToProperty`.
- Use `ReactiveCommand` for user operations. Derive `CanExecute` from observable state, bind `IsExecuting` to local progress feedback, and handle `ThrownExceptions` visibly.
- Keep UI state declarative: bind loading, connected, control, telemetry, and error states. Do not use imperative busy counters or control event handlers for application logic.
- Compose concurrent device, telemetry, and command streams into responsive status and availability state. Deduplicate in-flight commands, serialize conflicting operations, and transition persistent UI only after confirmed device outcomes.

## FTMS UX

- Keep all controls protocol-backed and functional. Do not show an actionable control unless the device advertises support and its required range or parameter constraints are known.
- Serialize Control Point requests, await their matching indications, and present pending, accepted, rejected, timed-out, and disconnected states.
- Preserve latest-only telemetry delivery and marshal bound collection changes to the MAUI main thread.
