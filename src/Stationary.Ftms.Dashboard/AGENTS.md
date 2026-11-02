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
- App menus live in Shell `MenuBarItems`. `AppDelegate.BuildMenu` applies them when MAUI finds no active window scene, then removes the system File, Format, View, and Help menus. Never reuse a system shortcut such as Cmd+H, Cmd+Q, or Cmd+W: UIKit rejects the conflicting menu and MAUI then drops every later app menu. Keep every action reachable from page UI too.

## Mac Catalyst Training Pages

- Workouts, Plans, and Assessment are Shell top tabs: one `Tab` with three `ShellContent` pages (`TrainingWorkoutsPage`, `TrainingPlansPage`, `TrainingAssessmentPage`) titled "Workouts". Keep the title and native toolbar back control above the tabs; do not repeat a header inside each tab. Do not reintroduce button-driven browse modes in `TrainingViewModel`.
- Keep the Training `ShellContent` pages lazy (`ContentTemplate`). Eager XAML materialization previously crashed the application during startup.
- Do not add a `Picker` to the Training pages; it previously crashed navigation to Training. Replace it only with a separately tested selector.
- Ending a ramp test, whether by Finish, End, or exhausting the steps, computes the FTP estimate and opens Results. The Results ramp panel is the only place to accept the estimate or keep the current FTP (`RampEstimateReviewed`); do not add Accept FTP elsewhere. The same panel offers the ramp's peak heart rate as max HR (`ApplyMaximumHeartRateCommand`).
- `StopWorkoutSession` saves every ride with effort through `SaveRideCommand` (`rides/`, `previous-session.json`, `ride-history.json`); do not add a separate save button. Ride analyses (`RideAnalytics`, `RideHistory`, `WorkoutAnalysis`, `SavedRideReport`) stay pure in Core and read the ride's stored `RideContext` before current settings.
- Charts of saved rides and trends set `HoverCommand`, `SelectionStartCommand`, `SelectionEndCommand`, `HoverTimestamp`, `SelectionStart`, and `SelectionEnd` to `{x:Null}` so they do not drive the live chart cursor.
- Apply the shared `ActionButton` style to every Training action, including ramp-test and currently unavailable actions. It is the established Sensors-page pattern and provides the correct enabled and disabled presentation.

- In-page tabs (such as the heart-rate zone method) use `SegmentedTabs`, which renders a native `UISegmentedControl` through `Platforms/MacCatalyst/SegmentedTabsHandler.cs`. Bind `Titles` and `SelectedIndex` one-way from the view model and route selection through `SelectionChangedCommand` (an `int` index). Do not restyle buttons or labels as tabs.

## 3D Ride Scene And Multi-Window

- Game rules stay pure in Core `RideGame`; `RideSceneViewModel` exposes the latest `RideSceneFrame`, window visibility (`ShouldShowWindow`), and the keep-awake state through ReactiveUI.
- `VulkanSceneRenderer` owns a dedicated render thread and reads only the latest frame; `Update` and `Resize` must never block the UI thread. MoltenVK configures the `CAMetalLayer` from that thread, so `MetalHostView` hosts it as a sublayer, never as the view's backing layer; UIKit asserts on off-main-thread changes to a backing layer.
- Shaders live in `Shaders/`. After editing one, recompile its committed SPIR-V in that folder: `glslangValidator -V <shader> -o <shader>.spv`.
- Frames render in passes: HDR scene (opaque, then mirrored reflections below the floor, then additive `Glow`/`Streak`), a bloom chain (`bloom_down.frag` then `bloom_up.frag` per level), and `composite.frag` (tone mapping and grading) into the sRGB swapchain.
- MoltenVK quirks: Metal3 argument buffers corrupt reads between the back-to-back bloom passes, so `VulkanSceneRenderer` sets `MVK_CONFIG_USE_METAL_ARGUMENT_BUFFERS=0` through libc `setenv` before `Vk.GetApi()` (`Environment.SetEnvironmentVariable` does not reach native `getenv`). Fast-math folds `isnan`/`isinf`, so test float bits instead; `pow` with a negative base returns NaN.
- DEBUG builds: `STATIONARY_SCENE_DEMO=1` drives the scene with simulated telemetry, and `STATIONARY_SCENE_CAPTURE=<folder>` also writes periodic PNG frames there (`Scene/SceneDemo.cs`).
- Each `SceneDraw` has a `SceneMaterial` whose value is the `effects.x` material constant in `scene.frag`; keep the enum and the shader constants in sync. `Glow` and `Streak` draw in a second, additive pass without depth writes. Keep push constants (`ScenePushConstants`) matching the shader `Push` block.
- `SceneWindowCoordinator` opens and closes the scene `Window` and moves it to a secondary display through `AppKitInterop` (Objective-C runtime calls on `NSWindow`/`NSScreen`). `Application.OpenWindow` does not call `App.CreateWindow`; a restored scene session does, and `App` closes that extra window.
- Multi-window requires `UIApplicationSceneManifest` in `Platforms/MacCatalyst/Info.plist`. Incremental builds can keep a stale bundle `Info.plist`; after editing the manifest, delete `obj/**/AppManifest.plist` and the bundle `Info.plist`, then rebuild.
- Scene menu items in the Display menu have no accelerators; Cmd+Shift+3 and similar are system shortcuts.

## Mac Catalyst Button Rendering

- Apply `ActionButton` or `SecondaryActionButton` to every dashboard button. Both styles use opaque command fills (`Accent` and `SecondaryAccent`); do not create page-local button fills or low-contrast surface buttons.
- Preserve both `ButtonHandler` mappings in `MauiProgram` for `IView.Background` and `IView.IsEnabled`.
- In the shared handler, set `UIButtonConfiguration.BaseBackgroundColor`, copy the value-style `Background` configuration, set its `BackgroundColor`, and assign it back. Mutating `configuration.Background.BackgroundColor` without reassignment does not reliably update native fills.
- Copy the MAUI button text into `UIButtonConfiguration.Title` and keep the `IText.Text` mapping. A configured `UIButton` ignores `SetTitle`, so a configuration without a title blanks the button whenever `IsEnabled` re-maps.
- Verify new button work in the running Mac Catalyst app. Check at least one `ActionButton`, one `SecondaryActionButton`, and a disabled button; all must render a full opaque fill.
- Known issue: disabled button titles currently render almost invisible. Pair every command whose `CanExecute` can be false with visible text explaining why.

## Reactive UI

- Use the installed System.Reactive and ReactiveUI packages for every dashboard state change: route user input and settings through `ReactiveCommand`, service events through observable pipelines, and derived state through bindings and `ToProperty`. Do not mutate bound state from code-behind, direct two-way controls, or imperative event handlers.
- Adapt service events with observables at the boundary; compose, schedule, and dispose subscriptions through ReactiveUI lifetimes.
- Model writable state with `ReactiveObject`; derive display and availability state with observable pipelines and `ToProperty`.
- Use `ReactiveCommand` for user operations. Derive `CanExecute` from observable state, bind `IsExecuting` to local progress feedback, and handle `ThrownExceptions` visibly.
- Workout execution is a pure reducer: `TrainingSessionEngine.Reduce(TrainingSession, TrainingSessionEvent)` in Core returns the next immutable state and `TrainingSessionEffect`s. `TrainingViewModel` folds events with `Scan`, interprets effects (BLE, navigation, persistence), and feeds results back as events. Add workout behavior to the engine and cover it with `TrainingSessionEngineTests`; do not add mutable session fields to the view model.
- Bind native entry, slider, and selection values one-way; use `ReactiveInput` command properties for edits. Its observable event subscriptions attach while loaded and detach when unloaded. `ReactiveSwitch` delegates to the same adapter.
- Custom controls adapt platform, lifecycle, and timer events with `Observable.FromEventPattern` scoped by `ReactiveInput.WhileLoaded`; do not attach imperative `+=` handlers in UI code.
- Serialize Shell presentation through `ReactiveOperationQueue`. Report navigation failures instead of swallowing exceptions; dispose the queue and navigation subscriptions at window teardown.
- Run training ticks through `AdvanceCommand` with `InvokeCommand`; observe automatic target and completion tasks through owned Rx subscriptions. Keep cadence checks responsive while a device write awaits acknowledgement; do not start detached tasks from interval callbacks.
- Recheck auto-apply target revisions when a queued write starts, not only when the slider emits. Reject non-finite targets and keep input errors visible.
- Keep UI state declarative: bind loading, connected, control, telemetry, and error states. Do not use imperative busy counters or control event handlers for application logic.
- Compose concurrent device, telemetry, and command streams into responsive status and availability state. Deduplicate in-flight commands, serialize conflicting operations, and transition persistent UI only after confirmed device outcomes.

## FTMS UX

- Keep all controls protocol-backed and functional. Do not show an actionable control unless the device advertises support and its required range or parameter constraints are known.
- Serialize Control Point requests, await their matching indications, and present pending, accepted, rejected, timed-out, and disconnected states.
- Preserve latest-only telemetry delivery and marshal bound collection changes to the MAUI main thread.
