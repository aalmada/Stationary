---
name: dotnet-maui
description: "Build, architect, debug, test, and publish .NET MAUI 10 cross-platform native apps for Android, iOS, Mac Catalyst, and Windows using C#, XAML, or C# markup. USE FOR: MAUI project configuration, single-project targeting, pages, layouts, controls, XAML, MVVM, data binding, CommunityToolkit.Mvvm, dependency injection, Shell navigation, handlers, platform integration, permissions, lifecycle, accessibility, localization, performance, trimming, Appium UI tests, Android/iOS/Windows publishing. DO NOT USE FOR: ASP.NET Core web UI, Blazor-only apps, Avalonia, WPF-only, Xamarin.Forms migration without MAUI target code, or generic .NET code with no MAUI UI or platform concern."
---

# .NET MAUI

Build one native app project for Android, iOS, Mac Catalyst, and Windows. Start with the target platform and user workflow; share UI and domain code, and isolate platform implementations.

## Workflow

1. Confirm target frameworks, supported OS versions, signing requirements, and physical-device test coverage.
2. Keep views declarative and thin; place presentation state and commands in view models.
3. Register services and view models in `MauiProgram.CreateMauiApp`; inject dependencies through constructors.
4. Use Shell with explicit, stable routes for app navigation. Validate and minimize navigation data.
5. Put platform-specific implementations behind shared interfaces. Add manifest, entitlement, or permission declarations before invoking device APIs.
6. Handle window lifecycle transitions: cancel resource-intensive work on `Stopped`, refresh/reconnect on `Resumed`, and dispose native subscriptions on `Destroying`.
7. Unit-test view models and services; run Appium UI tests for essential workflows on each target platform.
8. Test Release builds on real devices. Verify trimming, signing, store metadata, privacy declarations, and platform package output before publishing.

## Quick Reference

| Concern | Use | Avoid |
| --- | --- | --- |
| UI | XAML bindings, compiled bindings where applicable, resource dictionaries | Business logic in code-behind |
| Presentation logic | MVVM and commands | Control event handlers as application logic |
| Dependencies | `Microsoft.Extensions.DependencyInjection` constructor injection | Service locator calls in views/view models |
| Navigation | Shell routes and `GoToAsync` | Generated routes or duplicate route names |
| Cross-platform APIs | `Microsoft.Maui.Essentials` abstractions | Direct platform API calls from shared UI |
| Platform variation | `Platforms/`, partial classes, interfaces, handlers | Scattered `#if` blocks in domain code |
| Async work | Cancellation tokens and lifecycle-aware refresh | Fire-and-forget work tied to a page |
| Collections | Main-thread mutations | Editing bound `ObservableCollection<T>` on a worker thread |

## Project Shape

| Location | Responsibility |
| --- | --- |
| `MauiProgram.cs` | App bootstrap, dependency registrations, logging, fonts, handlers |
| `App.xaml` | Global resources and application root |
| `AppShell.xaml` | Root navigation hierarchy and explicit routes |
| `Views/` | Pages and reusable visual components |
| `ViewModels/` | Bindable state, commands, validation, presentation coordination |
| `Services/` | Data access, platform abstractions, external integrations |
| `Platforms/` | Platform manifest, entitlements, native implementations, lifecycle hooks |
| `Resources/` | Images, fonts, raw assets, styles, localization resources |

## Guardrails

- Do not hold page, view, activity, or native-control references in singleton services.
- Do not assume lifecycle events mean the process will be terminated or resumed; persist necessary state early.
- Treat UI thread affinity as a requirement for collection changes and native control access.
- Do not use `AddScoped` as a per-page lifetime unless you explicitly create and dispose scopes; MAUI has no automatic navigation scope.
- Request only the permissions needed immediately before the related feature, and handle denial.
- Use semantic properties, accessible names, keyboard paths, and platform accessibility inspectors.

## Reference Files

| File | Load When |
| --- | --- |
| [references/architecture-and-startup.md](references/architecture-and-startup.md) | Structuring a MAUI app, configuring the single project, bootstrapping `MauiProgram`, DI, logging, configuration, or handlers |
| [references/mvvm-bindings-and-ui.md](references/mvvm-bindings-and-ui.md) | Building XAML UI, MVVM state/commands, compiled bindings, resources, controls, accessibility, localization, or collection views |
| [references/shell-navigation.md](references/shell-navigation.md) | Defining Shell hierarchy/routes, navigating, passing data, deep links, back-stack behavior, or navigation lifecycle events |
| [references/platform-integration.md](references/platform-integration.md) | Accessing device APIs, implementing target-specific behavior, configuring handlers, declaring permissions, or storing secrets |
| [references/lifecycle-and-async.md](references/lifecycle-and-async.md) | Responding to window/platform lifecycle events, cancellation, backgrounding, resuming, or resource cleanup |
| [references/testing.md](references/testing.md) | Unit-testing view models/services, adding Appium UI tests, selecting emulators/devices, or validating platform behavior |
| [references/performance-and-trimming.md](references/performance-and-trimming.md) | Diagnosing startup/scrolling/memory issues, virtualizing lists, optimizing resources, or publishing a trimmed app |
| [references/deployment-and-publishing.md](references/deployment-and-publishing.md) | Signing, provisioning, packaging, versioning, and distributing Android, iOS, Mac Catalyst, or Windows apps |
| [references/best-practices.md](references/best-practices.md) | Reviewing cross-cutting architecture, UI, navigation, lifecycle, platform, testing, performance, and release decisions |
| [references/sources.md](references/sources.md) | Rechecking facts against the current official .NET MAUI documentation and samples |

## Sources

- [.NET MAUI product overview](https://dotnet.microsoft.com/apps/maui)
- [.NET MAUI documentation](https://learn.microsoft.com/dotnet/maui/?view=net-maui-10.0)
