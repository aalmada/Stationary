# Research Sources

Research baseline: ReactiveUI 24.2.0, ReactiveUI.SourceGenerators 3.2.0, standalone ReactiveUI.Primitives 7.4.0, ReactiveUI.Validation 7.1.0, and DynamicData 9.4.33. ReactiveUI 24.2.0 resolved ReactiveUI.Primitives 7.3.0 in compile validation. Recheck stable versions, target frameworks, dependencies, and public APIs when applying this skill.

## Source Precedence

Use sources in this order when they disagree:

1. The resolved NuGet package, matching release tag, reference assembly, and shipped public API for the selected distribution.
2. Source and tests at that release tag.
3. Current package README and platform sample for the same release line.
4. Current ReactiveUI website handbook and migration guides.
5. Older articles, books, and samples as conceptual guidance only.

The website contains valuable semantics but some pages still show pre-v24 `ReactiveUI` namespaces, `Unit`, `IScheduler`, `Observable`, or legacy initialization while discussing the default package. Convert each example to the selected distribution and compile it before adoption. Do not infer package compatibility from a documentation navigation table.

## Core and Releases

| Source | Use |
| --- | --- |
| [ReactiveUI repository](https://github.com/reactiveui/ReactiveUI) | Source, tests, samples, public API baselines, release engineering |
| [ReactiveUI package](https://www.nuget.org/packages/ReactiveUI) | Current default-family version, target frameworks, dependencies, distribution guidance |
| [ReactiveUI.Reactive package](https://www.nuget.org/packages/ReactiveUI.Reactive) | Current System.Reactive-family version and assets |
| [ReactiveUI documentation](https://www.reactiveui.net/documentation/) | Handbook, installation, migration, guidelines, and platform pages |
| [Installation matrix](https://www.reactiveui.net/documentation/getting-started/installation/) | Core and platform package names |
| [Minimum versions](https://www.reactiveui.net/documentation/getting-started/minimum-versions/) | Supported .NET, .NET Framework, Windows, Android, Apple, and Tizen baselines |
| [Upgrade overview](https://www.reactiveui.net/documentation/upgrading/) | Target-framework and legacy-feature migration overview |
| [Modern guidelines](https://www.reactiveui.net/documentation/guidelines/) | Current recommended initialization, generators, activation, architecture, and testing direction |

The repository `main` branch may contain changes newer than the latest stable package. When an API appears only on `main`, treat it as future work until the stable package or release tag contains it.

## Initialization and Platforms

| Source | Use |
| --- | --- |
| [RxAppBuilder handbook](https://www.reactiveui.net/documentation/handbook/rxappbuilder/) | Builder configuration, scheduler overrides, registration, container integration, `BuildApp` |
| [RxAppBuilder migration](https://www.reactiveui.net/documentation/upgrading/rxappbuilder-migration/) | Replacing static initialization and scattered registrations |
| [WPF setup](https://www.reactiveui.net/documentation/getting-started/installation/windows-presentation-foundation/) | `WithWpf`, reactive view bases, package setup |
| [Windows Forms setup](https://www.reactiveui.net/documentation/getting-started/installation/windows-forms/) | `WithWinForms`, `IViewFor<T>` forms, reactive user controls |
| [WinUI setup](https://www.reactiveui.net/documentation/getting-started/installation/winui/) | `WithWinUI`, reactive page/control bases, plain-window caveat |
| [MAUI setup](https://www.reactiveui.net/documentation/getting-started/installation/maui/) | `UseReactiveUI`, `WithMaui`, reactive page bases |
| [MAUI overview](https://www.reactiveui.net/documentation/guidelines/platform/maui-overview/) | MAUI-specific bindings, activation, Shell, platform APIs, handlers, samples, and migration links |
| [Blazor setup](https://www.reactiveui.net/documentation/getting-started/installation/blazor/) | Server/WASM builders and component base guidance |
| [Blazor overview](https://www.reactiveui.net/documentation/guidelines/platform/blazor/) | Reactive component bases, dependency-injected view models, activation, subscriptions, and disposable ownership |
| [ReactiveUI.Avalonia package](https://www.nuget.org/packages/ReactiveUI.Avalonia) | Independently versioned Avalonia integration, DI adapters, builder APIs |

Use current source/public API to verify platform builder methods. For example, current source distinguishes `.WithBlazor()` and `.WithBlazorWasm()`, and MAUI's `UseReactiveUI` callback builds the ReactiveUI configuration.

## View Models and Commands

| Source | Use |
| --- | --- |
| [View models](https://www.reactiveui.net/documentation/handbook/view-models/) | `ReactiveObject`, writable/read-only/output property roles |
| [WhenAny](https://www.reactiveui.net/documentation/handbook/when-any/) | Initial values, nested paths, notification providers, final-value distinctness |
| [OAPH](https://www.reactiveui.net/documentation/handbook/observable-as-property-helper/) | `ToProperty`, initial/deferred values, scheduler and error behavior |
| [Commands](https://www.reactiveui.net/documentation/handbook/commands/) | Factories, lazy execution, `CanExecute`, `IsExecuting`, output scheduling, errors |
| [Asynchronous command guideline](https://www.reactiveui.net/documentation/guidelines/framework/asynchronous-commands/) | Keeping async work inside command execution rather than `Subscribe` callbacks |
| [Default exception handler](https://www.reactiveui.net/documentation/handbook/default-exception-handler/) | `WithExceptionHandler`, fail-fast default, `RxState` |
| [ReactiveUI.SourceGenerators repository](https://github.com/reactiveui/ReactiveUI.SourceGenerators) | Current attributes, generated naming, compiler bands, diagnostics, v24 dual-family detection |
| [ReactiveUI.SourceGenerators package](https://www.nuget.org/packages/ReactiveUI.SourceGenerators) | Stable generator version and compatibility |
| [Fody migration](https://www.reactiveui.net/documentation/upgrading/fody-to-sourcegenerators/) | Attribute placement, `partial` types, `ToPropertyEx` conversion, cleanup |

## Views and Lifetimes

| Source | Use |
| --- | --- |
| [WhenActivated](https://www.reactiveui.net/documentation/handbook/when-activated/) | View/view-model activation, disposal ownership, lifetime examples |
| [Data binding](https://www.reactiveui.net/documentation/handbook/data-binding/) | `Bind`, `OneWayBind`, `BindCommand`, `BindTo`, converters, update signals |
| [Events](https://www.reactiveui.net/documentation/handbook/events/) | Event-to-observable guidance and obsolete event-package warning |
| [ReactiveUI.Primitives package](https://www.nuget.org/packages/ReactiveUI.Primitives) | Current lean operators, sequencers, disposables, observable-event generator, migration mappings |
| [Interactions](https://www.reactiveui.net/documentation/handbook/interactions/) | Handler chain, cold `Handle`, unhandled behavior, tests |
| [Routing](https://www.reactiveui.net/documentation/handbook/routing/) | `IScreen`, `RoutingState`, routable view models, routed hosts |
| [View location](https://www.reactiveui.net/documentation/handbook/view-location/) | `IViewFor`, registration, contracts, `ViewModelViewHost`, MAUI caveat |
| [ReactiveUI.Routing package](https://www.nuget.org/packages/ReactiveUI.Routing) | v24 separation of DynamicData-based routing helpers |

The current v24 source/public API wins over old Blazor guidance: inspect `ReactiveComponentBase<T>` before manually wiring per-property rerenders. The v24 `IViewLocator` also has four type/object and optional-contract overloads, replacing the older single generic instance overload shown in the handbook. Prefer explicit/source-generated view registration over reflection when publishing trimmed or AOT artifacts.

## Scheduling and Testing

| Source | Use |
| --- | --- |
| [Scheduling handbook](https://www.reactiveui.net/documentation/handbook/scheduling/) | Main/background scheduler roles and test replacement |
| [UI thread guideline](https://www.reactiveui.net/documentation/guidelines/framework/ui-thread-and-schedulers/) | Marshal view-model/UI delivery to the main scheduler |
| [Testing handbook](https://www.reactiveui.net/documentation/handbook/testing/) | Scheduler injection, `With`, command and OAPH tests |
| [ReactiveUI.Testing package](https://www.nuget.org/packages/ReactiveUI.Testing) | Default-family test assets |
| [ReactiveUI.Testing.Reactive package](https://www.nuget.org/packages/ReactiveUI.Testing.Reactive) | System.Reactive-family test assets and `TestScheduler` helpers |
| [Rx.NET skill](../../../../apm_modules/netfabric/intelligentium/plugins/dotnet-rx/.apm/skills/dotnet-rx/SKILL.md) | Observable contracts, System.Reactive operators, virtual time, multicasting, flow limits |

Testing public API files in the repository confirm that `SchedulerExtensions.With` targets `ISequencer` in the default package and `IScheduler` in `.Reactive`; only the `.Reactive` package exposes `Microsoft.Reactive.Testing.TestScheduler` helpers.

## Ecosystem

| Source | Use |
| --- | --- |
| [Collections handbook](https://www.reactiveui.net/documentation/handbook/collections/) | ReactiveUI's DynamicData integration guidance |
| [DynamicData repository](https://github.com/reactivemarbles/DynamicData) | Source/cache/list semantics, operators, binding, disposal, examples |
| [DynamicData package](https://www.nuget.org/packages/DynamicData) | Stable version, target frameworks, dependencies |
| [Validation handbook](https://www.reactiveui.net/documentation/handbook/user-input-validation/) | Validation rules, presentation, alternatives |
| [ReactiveUI.Validation repository](https://github.com/reactiveui/ReactiveUI.Validation) | Current Primitives migration, APIs, platform support |
| [ReactiveUI.Validation package](https://www.nuget.org/packages/ReactiveUI.Validation) | Stable version, dependencies, target frameworks |
| [Dependency injection handbook](https://www.reactiveui.net/documentation/handbook/dependency-inversion/) | Splat registration and external-container boundaries |
| [Logging handbook](https://www.reactiveui.net/documentation/handbook/logging/) | Splat logger adapters and observable diagnostics |

At this baseline, NuGet has no `ReactiveUI.Validation.Reactive` package. A compile probe using ReactiveUI.Validation 7.1.0 with ReactiveUI 24.2.0 default failed on the `IReactiveObject` contract despite successful restore; do not use that pair without a newer compatibility release. `ReactiveUI.Extensions` 4.0 is deprecated; NuGet directs consumers to ReactiveUI.Primitives.

## Verification Rules

1. Resolve the selected package family's namespaces and public types from the compiled reference assembly.
2. Match source/tests to the stable package tag; do not use moving `main` as shipped proof.
3. Compile representative state, command, binding, and startup examples for each supported target.
4. Inspect transitive packages for both `ReactiveUI` and `ReactiveUI.Reactive` before accepting an ecosystem dependency.
5. Publish and launch trimmed/AOT targets when using expressions, scanning, generated registrations, or platform heads.
6. Treat old `Unit`, `IScheduler`, `RxApp`, `Locator`, Fody, ReactiveList, and ReactiveUI.Events examples as migration inputs, not default v24 templates.
