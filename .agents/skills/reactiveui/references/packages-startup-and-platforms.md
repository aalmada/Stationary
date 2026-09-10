# Packages, Startup, and Platforms

## Baseline

Research baseline: ReactiveUI 24.2.0 and ReactiveUI.SourceGenerators 3.2.0. Resolve the actual package versions and read the matching tag or package documentation before relying on a signature. ReactiveUI 24 supports .NET 8, 9, and 10, plus .NET Framework 4.6.2 through 4.8.1 through `net462`, `net472`, and `net481` assets. Platform packages impose additional requirements.

ReactiveUI 24 removed the writable static `RxApp` API. Configure startup through `RxAppBuilder`; read application schedulers from `RxSchedulers` and the default exception handler from `RxState`.

## Distribution Decision

| Concern | Default family | System.Reactive family |
| --- | --- | --- |
| Core package | `ReactiveUI` | `ReactiveUI.Reactive` |
| Core namespace | `ReactiveUI` | `ReactiveUI.Reactive` |
| Builder namespace | `ReactiveUI.Builder` | `ReactiveUI.Reactive.Builder` |
| Empty value | `RxVoid` | `System.Reactive.Unit` |
| Scheduling contract | `ISequencer` | `IScheduler` |
| Pushable source | `Signal<T>` | `Subject<T>` |
| Test package | `ReactiveUI.Testing` | `ReactiveUI.Testing.Reactive` |
| Extra routing helpers | `ReactiveUI.Routing` | `ReactiveUI.Routing.Reactive` |

Both families use the ReactiveUI.Primitives engine. Choose the default for a new lean or trimming-sensitive app. Choose `.Reactive` when source or public API already uses System.Reactive types. Do not reference both ReactiveUI sibling families in one project accidentally.

A direct System.Reactive or DynamicData dependency can coexist with the default family because `IObservable<T>` is a BCL interface, but overlapping operator and `Subscribe` extensions can become ambiguous. Select one operator surface per file or isolate the boundary.

## Package Map

| Target | Default package | System.Reactive package |
| --- | --- | --- |
| WPF | `ReactiveUI.WPF` | `ReactiveUI.WPF.Reactive` |
| Windows Forms | `ReactiveUI.WinForms` | `ReactiveUI.WinForms.Reactive` |
| WinUI 3 | `ReactiveUI.WinUI` | `ReactiveUI.WinUI.Reactive` |
| .NET MAUI | `ReactiveUI.Maui` | `ReactiveUI.Maui.Reactive` |
| Blazor | `ReactiveUI.Blazor` | `ReactiveUI.Blazor.Reactive` |
| AndroidX | `ReactiveUI.AndroidX` | matching `.Reactive` package when published for the selected version |
| Avalonia | `ReactiveUI.Avalonia` | `ReactiveUI.Avalonia.Reactive` |

Avalonia integration is independently versioned; verify its compatibility matrix instead of forcing the core ReactiveUI version onto it. Add `ReactiveUI.SourceGenerators` with `PrivateAssets="all"` to projects containing generated declarations. ReactiveUI packages exclude ReactiveUI.Primitives analyzers transitively; reference the Primitives package directly only when opting into those analyzers or APIs.

Core routing types remain in the main package. Add `ReactiveUI.Routing` only for DynamicData-based routing, collection, or persistence helpers introduced into that separate package.

## Composition Root

Default-family WPF setup:

```csharp
using ReactiveUI.Builder;

_ = RxAppBuilder.CreateReactiveUIBuilder()
    .WithWpf()
    .RegisterView<MainView, MainViewModel>()
    .WithRegistration(static resolver =>
    {
        resolver.RegisterLazySingleton<IDataService>(static () => new DataService());
    })
    .BuildApp();
```

For the System.Reactive family, import `ReactiveUI.Reactive.Builder` and use matching `.Reactive` packages. Call `BuildApp()` once before constructing reactive views or resolving services.

| Platform | Startup shape |
| --- | --- |
| WPF | `RxAppBuilder.CreateReactiveUIBuilder().WithWpf().BuildApp()` during application startup |
| Windows Forms | `.WithWinForms()` before `Application.Run` |
| WinUI 3 | `.WithWinUI()` in the application constructor/startup path |
| MAUI | `MauiAppBuilder.UseReactiveUI(rx => rx.WithMaui())` |
| Blazor Server | `.WithBlazor()` before building the host |
| Blazor WebAssembly | `.WithBlazorWasm()` before building the host |
| Avalonia | Avalonia `AppBuilder.UseReactiveUI(...)`, then register views through its integration extensions |

Do not call `.WithMaui()` twice: `UseReactiveUI` builds the supplied ReactiveUI builder. Follow the selected platform package's current startup example because host lifecycle and dispatcher capture differ.

## Registration and DI

- Prefer constructor injection in view models. Keep Splat/AppLocator access at the composition or framework-integration boundary.
- Use `WithRegistration` for Splat registrations or a supported Splat adapter when the application already owns another container.
- Register stateful services as singletons only when their lifetime is truly application-wide; make screens and transient view models explicit.
- Register views with `RegisterView<TView, TViewModel>` or source-generated registration when trimming or Native AOT matters.
- `WithViewsFromAssembly` and reflection-based registration carry `RequiresUnreferencedCode`; do not use them as the AOT-safe path.
- Source-generated `[IViewFor<T>]` registrations require a non-`None` registration type and a startup call to `RegisterViewsForViewModelsSourceGenerated()`.

Explicit registration solves view discovery only. ReactiveUI 24 also annotates expression-based `WhenAny`, `Bind`, `BindCommand`, `BindInteraction`, and `BindTo` paths with `RequiresUnreferencedCode`; binding implementation paths can require dynamic code. Preserve required members or choose a supported generated/compiled alternative, treat publish warnings as actionable, and launch-test the trimmed/AOT artifact.

## Platform Boundaries

| Platform | Typical reactive view base / caveat |
| --- | --- |
| WPF | `ReactiveWindow<T>` or `ReactiveUserControl<T>` |
| Avalonia | `ReactiveWindow<T>` or `ReactiveUserControl<T>` from its integration package |
| MAUI | `ReactiveContentPage<T>`; `ViewModelViewHost` item templating is not supported like desktop XAML |
| WinUI 3 | `ReactivePage<T>` or `ReactiveUserControl<T>` inside a plain `Window`; there is no reactive window base |
| Windows Forms | `ReactiveUserControl<T>`; forms implement `IViewFor<T>` directly |
| Blazor | `ReactiveComponentBase<T>`; current v24 bases observe view-model property changes and manage component activation |

Keep shared view models free of controls, dispatchers, pages, windows, and platform navigation objects. Put platform adapters and views in the platform head.

## Package Review

1. Confirm every project uses one distribution family.
2. Match core, platform, routing, and testing package versions where those packages release together.
3. Check source-generator/compiler compatibility independently from the runtime package.
4. Verify every UI head references its platform package; the core package alone does not register platform services.
5. Build every target framework and publish mode, including trimmed/AOT modes that the application supports.
