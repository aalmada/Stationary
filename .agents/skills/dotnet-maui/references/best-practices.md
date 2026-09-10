# .NET MAUI Best Practices

Use this reference for implementation and review decisions in .NET MAUI 10 applications.

## Architecture

| Layer | Owns | Must Not Own |
| --- | --- | --- |
| View | XAML structure, visual states, bindings, accessibility semantics | Business rules, data access, navigation decisions |
| View model | Presentation state, commands, validation, orchestration | `Page`, `View`, native controls, platform activity/window types |
| Service | Network, persistence, device capability abstraction, domain operations | UI state or visual navigation |
| Platform implementation | Native APIs, manifests, capabilities, platform lifecycle hooks | Shared application policy duplicated per platform |

- Prefer MVVM when an app has non-trivial state or behavior. Use a source-generator MVVM toolkit when it reduces boilerplate consistently across the app.
- Keep code-behind for view-only behavior that cannot be expressed declaratively. Move user actions into commands.
- Bind a page to its injected view model at construction. Avoid resolving services ad hoc from `IServiceProvider`.
- Register durable, stateless shared services as singletons. Register pages and navigation-created view models as transients unless state must intentionally persist.
- Do not use `AddScoped` for per-page dependencies without explicit `IServiceScopeFactory` scope creation and disposal.

## Binding And UI

- Set `x:DataType` for compiled bindings on pages, data templates, and reusable controls when practical.
- Use `INotifyPropertyChanged` for bindable state and commands for user actions. Keep `CanExecute` state current.
- MAUI marshals `PropertyChanged` binding updates to the UI thread, but it does not marshal `ObservableCollection<T>.CollectionChanged` mutations. Dispatch collection edits with `MainThread.BeginInvokeOnMainThread` or replace the collection through a property notification.
- Use `CollectionView` for virtualized item lists. Keep item templates shallow, measure images, and avoid expensive converters or synchronous I/O in bindings.
- Define colors, typography, spacing, and control styles in resource dictionaries. Test visual states, light/dark themes, font scaling, orientation, and window resizing.
- Add semantic descriptions, focus order, keyboard interactions, and sufficient color contrast before UI testing.

## Navigation

- Use Shell for an application-level navigation hierarchy. Give all hierarchy items explicit `Route` values because generated routes are not stable across sessions.
- Register detail pages with `Routing.RegisterRoute` before navigation. Keep route names unique; duplicate routes throw `ArgumentException`.
- Use `await Shell.Current.GoToAsync(...)` and preserve asynchronous error handling and cancellation where the calling workflow needs it.
- Pass identifiers or small immutable values, not large mutable objects, as navigation data. Validate and authorize all received values before loading data.
- Keep absolute routes (`//`) for root navigation and relative routes for contextual flows. Verify back-stack behavior for tabs, deep links, login/logout, and cancellation.

## Platform Integration

| Need | Preferred Boundary |
| --- | --- |
| Common device capability | MAUI Essentials API behind an application service when it affects business flow |
| Native-only API | Interface in shared code; implementation under `Platforms/<target>/` |
| Minor visual divergence | Platform-specific XAML/C# in the view layer or a custom handler |
| Native control customization | Handler mapper, scoped to the target control and target platform |
| Build configuration | Conditions in the single `.csproj`; do not split shared code into copy-pasted projects |

- Declare Android permissions in `AndroidManifest.xml`; add iOS/Mac Catalyst usage descriptions and capabilities/entitlements before accessing protected APIs.
- Request runtime permissions as close as possible to the feature that needs them. Explain denial and provide a non-blocking path where feasible.
- Store small secrets with `SecureStorage`; never log credentials, tokens, location, or personal data.
- Validate external input from deep links, share targets, sensors, files, and platform callbacks.
- Keep `#if` localized. Prefer an interface or partial implementation when platform differences contain behavior.

## Lifecycle And Async Work

- Subscribe through `Window` lifecycle events or a `Window` subclass. Treat `Stopped` as a resource-conservation boundary: cancel requests, pause streams, and release exclusive resources.
- On `Resumed`, re-establish subscriptions and refresh visible data. Do not assume `Resumed` runs on first launch.
- On `Destroying`, detach subscriptions to native windows and controls. Persist critical user state before the OS may terminate the app.
- Pass `CancellationToken` through network, database, and long-running operations. Cancel page or workflow work when it is no longer relevant.
- Surface expected failures in view-model state. Never let `async void` escape except required UI event signatures.

## Testing, Performance, And Release

| Area | Required Check |
| --- | --- |
| Unit tests | View models, validation, services, error/cancellation paths with mocked dependencies |
| UI tests | Essential workflows through Appium on every supported platform family |
| Devices | Physical-device validation for permissions, sensors, notifications, performance, and release signing |
| Performance | Startup, navigation, scrolling, memory, images, network latency, and battery use in Release mode |
| Release | Release trimming, signing, app IDs, versioning, store assets, privacy disclosures, and package installation |

- Measure before optimizing. Prefer simple layouts, virtualization, asynchronous I/O, cached images, and cancellation over manual control micro-optimizations.
- Treat trimming warnings as release defects. Preserve only the dynamically accessed code that is demonstrably required, then test the trimmed package.
- Android store distribution uses an AAB; iOS uses an IPA and provisioning profile; Mac Catalyst produces an APP or PKG; Windows can use an MSIX package or unpackaged deployment.
- Run the signed, packaged artifact on a clean or representative device. A successful Debug deployment is not a release validation.

## Review Checklist

1. Are presentation, domain, and platform concerns separated at testable boundaries?
2. Are DI registrations intentional, with no accidental singleton view/page/native state?
3. Are Shell routes explicit, unique, awaited, and safe for deep-link input?
4. Are bound collection mutations and native UI updates dispatched to the main thread?
5. Are permissions, entitlements, secrets, and lifecycle cancellation correct per target?
6. Do Release builds, physical-device checks, accessibility review, and UI tests cover the changed workflow?

## Sources

- [.NET MAUI documentation](https://learn.microsoft.com/dotnet/maui/?view=net-maui-10.0)
- [Dependency injection](https://learn.microsoft.com/dotnet/maui/fundamentals/dependency-injection?view=net-maui-10.0)
- [Shell navigation](https://learn.microsoft.com/dotnet/maui/fundamentals/shell/navigation?view=net-maui-10.0)
- [App lifecycle](https://learn.microsoft.com/dotnet/maui/fundamentals/app-lifecycle?view=net-maui-10.0)
- [Deployment and testing](https://learn.microsoft.com/dotnet/maui/deployment/?view=net-maui-10.0)
