# Stationary.Ftms.Dashboard

Mac Catalyst MAUI dashboard for connecting to FTMS equipment and presenting telemetry and supported controls.

## Commands

- Build: `dotnet build src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj --no-restore -p:ValidateXcodeVersion=false`
- Build the Apple silicon bundle: `dotnet build src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj -f net11.0-maccatalyst -r maccatalyst-arm64 --no-restore -p:ValidateXcodeVersion=false`
- Run on Apple silicon: `dotnet build src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj -t:Run -f net11.0-maccatalyst -r maccatalyst-arm64 --no-restore -p:ValidateXcodeVersion=false`. `-t:Run` can launch a stale bundle; build the bundle first after source changes.

## Navigation And Ride Flow

- Use native navigation bars and the shared `ToolbarItem` back control from `AppShell.WithBackNavigation`; let the platform choose its placement. Do not add custom navigation renderers or in-page back buttons. Keep page titles in the native bar rather than repeating them in content.
- The Shell has no flyout: Home (ride choice and readiness), Ride (live dashboard), Workouts (lazy library), Results. Pairing and Settings are form-sheet modals wrapped in `NavigationPage`, opened through `DashboardDestination.Sensors` (FTMS machine), `HeartRateSensor` (Heart Rate Service), and `Settings`, and closed with `CloseSheetCommand`, so they never replace the ride dashboard.
- Keep FTMS and heart-rate pairing in separate sheets; they are different Bluetooth profiles with independent connections.
- Starting any ride (free ride, workout, ramp test, or a machine-reported start) navigates to Ride immediately, before awaiting BLE acknowledgements. Show pending, rejected, and timed-out starts on the Ride page with Retry and End.
- Keep the Ride page charts visible in every mode without scrolling; put secondary panels in the side column.
- Shell `MenuBarItems` do not currently appear on Mac Catalyst; keep every action reachable from page UI.

## Mac Catalyst Training Pages

- Workouts, Plans, and Assessment are Shell top tabs: one `Tab` with three `ShellContent` pages (`TrainingWorkoutsPage`, `TrainingPlansPage`, `TrainingAssessmentPage`) titled "Workouts". Keep the title and native toolbar back control above the tabs; do not repeat a header inside each tab. Do not reintroduce button-driven browse modes in `TrainingViewModel`.
- Keep the Training `ShellContent` pages lazy (`ContentTemplate`). Eager XAML materialization previously crashed the application during startup.
- Do not add a `Picker` to the Training pages; it previously crashed navigation to Training. Replace it only with a separately tested selector.
- Apply the shared `ActionButton` style to every Training action, including ramp-test and currently unavailable actions. It is the established Sensors-page pattern and provides the correct enabled and disabled presentation.

## Mac Catalyst Button Rendering

- Apply `ActionButton` or `SecondaryActionButton` to every dashboard button. Both styles use opaque command fills (`Accent` and `SecondaryAccent`); do not create page-local button fills or low-contrast surface buttons.
- Preserve both `ButtonHandler` mappings in `MauiProgram` for `IView.Background` and `IView.IsEnabled`.
- In the shared handler, set `UIButtonConfiguration.BaseBackgroundColor`, copy the value-style `Background` configuration, set its `BackgroundColor`, and assign it back. Mutating `configuration.Background.BackgroundColor` without reassignment does not reliably update native fills.
- Verify new button work in the running Mac Catalyst app. Check at least one `ActionButton`, one `SecondaryActionButton`, and a disabled button; all must render a full opaque fill.
- Known issue: disabled button titles currently render almost invisible. Pair every command whose `CanExecute` can be false with visible text explaining why.

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
