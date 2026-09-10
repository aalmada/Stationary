# Architecture And Startup

## Composition Root

- Build the app in `MauiProgram.CreateMauiApp`. Configure fonts, logging, handlers, configuration, and dependency registrations before `builder.Build()`; registrations are immutable afterward.
- Keep `App.xaml` for application resources and `AppShell.xaml` for root navigation. Create `Window` instances in `App.CreateWindow` when window lifecycle or multi-window behavior needs control.
- Use the single-project model: shared code and resources live at the root, while target configuration and native files live below `Platforms/`.
- Keep domain logic independent of MAUI types so it can be unit-tested without a device or UI host.

## Dependency Injection

| Registration | Use For | Avoid For |
| --- | --- | --- |
| `AddSingleton` | Stateless shared services, caches, settings, clients | Views, pages, native controls, user-specific transient state |
| `AddTransient` | Pages, view models, short-lived operations | A shared cache or long-lived connection owner |
| `AddScoped` | Explicitly created/disposed `IServiceScope` work units | Assumed per-page/per-navigation scope |

- Constructor-inject dependencies into services and view models. Register interface-to-implementation mappings at the composition root.
- MAUI has no automatic navigation scope; non-Blazor `AddScoped` services effectively behave as application singletons unless the app explicitly creates scopes.
- Do not resolve arbitrary dependencies from `IServiceProvider` in views or view models. Use a factory only when runtime selection is genuinely necessary.

## Project Configuration

- Target only platforms the product supports with `TargetFrameworks`; verify each target's supported OS and SDK workload in CI.
- Use conditional MSBuild properties or item groups for target configuration. Keep conditional compilation at platform boundaries.
- Add fonts with `ConfigureFonts`, images as `MauiImage`, raw files as `MauiAsset`, and app icons/splash screens with MAUI asset items.
- Add structured logging during startup and avoid logging secrets or personal data.

## Sources

- [Dependency injection](https://learn.microsoft.com/dotnet/maui/fundamentals/dependency-injection?view=net-maui-10.0)
- [Single project](https://learn.microsoft.com/dotnet/maui/fundamentals/single-project?view=net-maui-10.0)
- [App startup](https://learn.microsoft.com/dotnet/maui/fundamentals/app-startup?view=net-maui-10.0)
