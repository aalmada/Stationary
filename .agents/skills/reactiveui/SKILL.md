---
name: reactiveui
description: "Design, implement, migrate, debug, review, and test ReactiveUI 24+ applications. USE FOR: ReactiveObject; WhenAnyValue; ObservableAsPropertyHelper/ToProperty; ReactiveCommand; WhenActivated; Bind/OneWayBind/BindCommand; Interaction; routing and view location; RxAppBuilder; source generators; ReactiveUI.Primitives vs *.Reactive distributions; schedulers; DynamicData; validation; WPF, Avalonia, MAUI, WinUI, WinForms, Blazor. DO NOT USE FOR: generic Rx.NET pipelines without MVVM (use dotnet-reactive-extensions); unrelated MVVM frameworks; visual UI design."
---

# ReactiveUI

Build testable MVVM applications by expressing state, work, and lifetime as observable relationships. Verify examples against the installed package version: ReactiveUI 24 introduced a distribution split that makes many older imports and type signatures incomplete.

## Anatomy

| File | Purpose | Target Size |
| --- | --- | --- |
| `SKILL.md` | Routing, workflow, distribution choice, and invariants | <100 lines |
| `references/*.md` | ReactiveUI-specific APIs, platform setup, examples, and review checks | <200 lines each |

## Workflow

1. For structural codebase queries, complete the [CBM readiness preflight](../codebase-memory/references/integration-guide.md#mandatory-readiness-owner-and-preflight), then use its CLI; read project files directly only for configuration or non-indexed text.
2. Inspect the target framework, UI platform, ReactiveUI version, package graph, trimming/AOT requirements, DI container, and public uses of `Unit`, `IScheduler`, subjects, or ReactiveUI.Primitives types.
3. Choose one v24 distribution before writing imports or package references; keep core, platform, routing, and testing packages in that family.
4. Initialize ReactiveUI once at the composition root with `RxAppBuilder` and the platform extension. Prefer explicit view registration when trimming or Native AOT matters.
5. Model writable state, derived state, commands, interactions, collections, and navigation with the owning ReactiveUI abstraction; keep controls and dialogs in views.
6. Put view bindings, interaction handlers, event subscriptions, and other view-owned work inside `WhenActivated`; dispose each returned handle with the activation scope.
7. Make UI/background transitions explicit and inject time or schedulers used by testable behavior. A command's output scheduler does not schedule its execution body.
8. Test view models without UI objects; control time, execute commands by subscribing or awaiting, handle interactions in the test, and assert errors and disposal.

## Choose the Distribution First

| Need | Package family | Public reactive types |
| --- | --- | --- |
| New, lean, trimming-friendly application | `ReactiveUI`, `ReactiveUI.<Platform>` | `RxVoid`, `ISequencer`, `Signal<T>` |
| Existing System.Reactive API or source compatibility | `ReactiveUI.Reactive`, `ReactiveUI.<Platform>.Reactive` | `Unit`, `IScheduler`, `Subject<T>` |

The families share the ReactiveUI feature set but expose different namespaces and interop types. Do not combine sibling families accidentally. Use `ReactiveUI`/`ReactiveUI.Builder` for the default family and `ReactiveUI.Reactive`/`ReactiveUI.Reactive.Builder` for the System.Reactive family; verify platform namespaces from the installed package.

## Ownership Map

| Concern | Preferred abstraction |
| --- | --- |
| Writable view-model state | `ReactiveObject` plus `[Reactive]` or `RaiseAndSetIfChanged` |
| Derived read-only state | `WhenAnyValue` pipeline plus OAPH/`ToProperty` or `[ObservableAsProperty]` |
| User-triggered or asynchronous work | `ReactiveCommand`, `CanExecute`, `IsExecuting`, `ThrownExceptions` |
| Dialog or UI response requested by a view model | `Interaction<TInput, TOutput>` handled by the active view |
| View lifetime | `IViewFor<T>`, `WhenActivated`, `DisposeWith` |
| Dynamic collection projection | DynamicData source plus change-set operators; marshal only immediately before `Bind` |
| View-model-first navigation | `IScreen`, `RoutingState`, `IRoutableViewModel`, view location, routed host |

## Guardrails

- `ReactiveCommand.Execute()` and `Interaction.Handle()` are lazy; subscribe or await them.
- Handle expected command failures through `ThrownExceptions`; an unobserved failure reaches the default exception handler and crashes by default.
- A command excludes only its own concurrent executions. Coordinate separate commands that mutate one resource, and pass each execution token through admission, active work, and linked timeouts.
- Keep `CanExecute` non-failing and on the UI context when its source can emit elsewhere.
- Treat OAPH as a subscription: handle source errors upstream and choose its notification scheduler deliberately.
- Give application-lifetime view models deterministic teardown when they own commands, OAPHs, subscriptions, subjects, or asynchronous resources.
- Use `WhenAnyValue` only on properties with supported change notification; nested null intermediates suppress emissions until the chain becomes evaluable.
- Prefer `ReactiveUI.SourceGenerators` for new code. Treat ReactiveUI.Fody and manual global initialization as migration paths, not defaults.
- Explicit view registration removes assembly scanning; it does not remove v24 trimming/dynamic-code annotations from expression-based observation and binding APIs. Validate the published artifact.
- Load the [Rx.NET skill](../dotnet-reactive-extensions/SKILL.md) for generic observable contracts, operator semantics, multicasting, backpressure limits, and System.Reactive testing details.

## Reference Files

| File | Load When |
| --- | --- |
| [references/packages-startup-and-platforms.md](references/packages-startup-and-platforms.md) | Selecting a v24 distribution, packages, namespaces, `RxAppBuilder`, DI integration, platform setup, trimming, or AOT registration |
| [references/view-model-state-and-generators.md](references/view-model-state-and-generators.md) | Designing `ReactiveObject` state, `WhenAnyValue`, OAPH, computed properties, or source-generator declarations |
| [references/commands-and-errors.md](references/commands-and-errors.md) | Creating, invoking, canceling, scheduling, composing, or testing `ReactiveCommand`; handling `ThrownExceptions` |
| [references/activation-and-lifetimes.md](references/activation-and-lifetimes.md) | Owning subscriptions, bindings, handlers, and start/stop behavior with activation and disposal |
| [references/views-bindings-and-events.md](references/views-bindings-and-events.md) | Implementing `IViewFor<T>`, typed bindings, converters, command binding, generated observable events, or platform view bases |
| [references/interactions-routing-and-location.md](references/interactions-routing-and-location.md) | Requesting UI responses, navigating, registering views, resolving contracts, or choosing routing boundaries |
| [references/collections-validation-and-services.md](references/collections-validation-and-services.md) | Using DynamicData, ReactiveUI.Validation, dependency injection, logging, or application services |
| [references/scheduling-and-testing.md](references/scheduling-and-testing.md) | Controlling UI delivery, background work, virtual time, test schedulers, commands, interactions, and lifecycle tests |
| [references/migration-and-review.md](references/migration-and-review.md) | Upgrading legacy ReactiveUI code or reviewing architecture, threading, errors, leaks, AOT, and package consistency |
| [references/sources.md](references/sources.md) | Rechecking versions and claims against ReactiveUI source, package metadata, handbook pages, and ecosystem repositories |
