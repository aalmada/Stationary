# Migration and Review

## Upgrade Order

Migrate one vertical slice at a time and keep it compiling:

1. Inventory target frameworks, platform heads, ReactiveUI/Rx/DynamicData packages, namespaces, generated code, startup registration, and public reactive types.
2. Choose the v24 distribution. Use `.Reactive` first when preserving `Unit`, `IScheduler`, subjects, and existing System.Reactive source is the priority.
3. Align core, platform, routing, and testing packages; verify independently versioned Avalonia, source-generator, validation, and DynamicData compatibility.
4. Replace legacy initialization with one `RxAppBuilder` composition root.
5. Migrate Fody declarations to source generators and inspect generated members.
6. Move view bindings, event subscriptions, and interaction handlers into activation scopes.
7. Replace obsolete collections, errors, and event packages.
8. Run view-model, lifecycle, platform, and trimmed/AOT publish tests before removing compatibility code.

Do not combine a package-major upgrade, distribution conversion, DI-container replacement, and architecture rewrite in one unvalidated change.

## Legacy Mapping

| Legacy shape | Current direction |
| --- | --- |
| `RxApp.MainThreadScheduler = ...` | `RxAppBuilder.WithMainThreadScheduler(...)`; read through `RxSchedulers` |
| `RxApp.TaskpoolScheduler = ...` | `WithTaskPoolScheduler(...)`; read through `RxSchedulers` |
| `RxApp.DefaultExceptionHandler = ...` | `WithExceptionHandler(...)`; read through `RxState.DefaultExceptionHandler` |
| `RxApp.EnsureInitialized()` | Configure and call `BuildApp()` once; `RxAppBuilder.EnsureInitialized()` only verifies builder initialization |
| Scattered `Locator` registrations | Central `WithRegistration` and `AppLocator`, or one external-container adapter |
| Reflection view scanning in AOT app | Explicit builder or source-generated view registration |
| `ReactiveUI.Fody` attributes on properties | `ReactiveUI.SourceGenerators` on fields/partial declarations; make containing type `partial` |
| Fody `ToPropertyEx` | Generated helper plus `ToProperty` |
| `ReactiveList<T>` | DynamicData `SourceCache`/`SourceList`, expose a read-only projection |
| `UserError` | `Interaction<TInput, TOutput>` |
| `ReactiveUI.Events.*` | Current observable-event source generator |
| `ReactiveUI.Extensions` | `ReactiveUI.Primitives` helper surface; package is deprecated |
| Xamarin heads | Supported .NET MAUI/native platform packages |

Delete Fody package references and `FodyWeavers.xml` only after every generated property/OAPH has a source-generator equivalent. Attribute placement changes: Fody decorated auto-properties; source generation normally decorates private fields or supported partial properties.

## Distribution Migration

### Preserve System.Reactive Source

Reference `ReactiveUI.Reactive`, matching platform/testing/routing `.Reactive` packages, and import `ReactiveUI.Reactive` plus `ReactiveUI.Reactive.Builder`. This retains `Unit`, `IScheduler`, and subject-facing APIs while using the v24 implementation.

Check ecosystem packages individually. A package that depends on default `ReactiveUI` can pull both sibling cores into a `.Reactive` app. ReactiveUI.Validation 7.1 has no `.Reactive` sibling and is compile-incompatible with ReactiveUI 24.2 even in the default family; wait for an explicitly compatible release or use another validator.

### Move to the Default Family

Convert public boundaries deliberately:

| System.Reactive type | Default-family type |
| --- | --- |
| `Unit` | `RxVoid` |
| `IScheduler` | `ISequencer` |
| `Subject<T>` | `Signal<T>` |
| `BehaviorSubject<T>` | `StateSignal<T>` |
| `CompositeDisposable` | Default-family disposable container |
| `TestScheduler` | Primitives virtual clock/testing tools |

Keep BCL `IObservable<T>` boundaries where possible. Convert factories/operators/schedulers a slice at a time, remove ambiguous imports, and delete System.Reactive only after direct and transitive usage is understood.

## Architecture Review

| Area | Failure signals |
| --- | --- |
| State | Side effects in setters; duplicate mutable and OAPH writers; missing notifications |
| Commands | `async` work inside `Subscribe`; direct `Execute()` with no subscriber; ignored `ThrownExceptions`; UI code in view model |
| Lifetimes | Bindings outside activation; long-lived service subscription without disposal; duplicated handlers after navigation |
| Scheduling | UI mutation from worker callback; `ObserveOn` too early; assuming command output scheduler runs the body |
| Collections | Public mutable source; UI scheduling before transforms; unbounded cache/materialization; missing `DisposeMany` |
| Routing | Unsubscribed navigation command; singleton view; missing contract mapping; router ownership unclear |
| DI | Locator calls throughout view models; hidden fallback construction; parallel containers |
| AOT | Assuming explicit view registration also makes expression-based observation/binding trim-safe; ignored `RequiresUnreferencedCode`/`RequiresDynamicCode` warnings |
| Errors | Empty exception handlers; global handler used as recovery; OAPH/can-execute stream terminates silently |

## Behavioral Review

1. Trace each user action from binding/event to command, service, result state, and error presentation.
2. Identify the owner and lifetime of every subscription, binding, source, handler, and mutable collection.
3. Mark every scheduler boundary and verify only UI delivery runs on the UI scheduler.
4. Check every async path for cancellation, stale-result policy, overlap policy, and terminal error handling.
5. Exercise null property chains, initial OAPH values, inactive views, repeated activation, and navigation back/reset.
6. Verify view location for each contract and publish mode.

## Performance Review

- Do not optimize by removing required notification, serialization, or disposal semantics.
- Share an expensive cold property stream only when multiple subscribers must share one execution; define replay and disconnect behavior explicitly.
- Prefer OAPH and DynamicData incremental binding over repeated whole-list assignment.
- Keep `ObserveOn` after expensive DynamicData and observable transforms.
- Bound replay, cache, grouping, retry, and concurrent work.
- Use `nameof` OAPH overloads and source-generated registration where measurement or AOT constraints justify them.
- Measure startup/reflection scanning, binding churn, large change sets, and activation cycles on the target platform.

## Validation Gates

- Restore and build every target framework with warnings enabled.
- Inspect generated source and analyzer diagnostics.
- Run deterministic view-model, command, scheduler, interaction, routing, and activation tests.
- Run platform smoke tests for binding, view location, and dispatcher access.
- Publish with the application's trimming/AOT settings and launch the artifact.
- Inspect the resolved dependency graph for both ReactiveUI core families and incompatible Rx versions.
- Verify no legacy static initialization, Fody files, ReactiveList, or obsolete event package remains unless documented as an intentional bridge.
