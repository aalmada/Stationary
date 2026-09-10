# Activation and Lifetimes

## Activation Model

`WhenActivated` creates a fresh disposable scope each time a view or activatable view model becomes active. Deactivation disposes that scope. Reactivation runs the callback again, so setup inside it must tolerate repetition.

Use activation to align observable work with visibility or navigation, not as a universal replacement for object lifetime.

| Owner / work | Lifetime |
| --- | --- |
| View binding or control event | View activation |
| Interaction handler supplied by a view | View activation |
| Subscription from a view to a view model | View activation |
| Polling/location/sensor feed needed only while visible | View-model activation |
| Application-wide service feed | Service/application lifetime |
| Finite task/command execution | Natural completion, unless deactivation should cancel it |
| Pure constructor relationship among properties | View-model lifetime |

## Activatable View Model

```csharp
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Disposables;

public sealed class DashboardViewModel : ReactiveObject, IActivatableViewModel
{
    public DashboardViewModel(IMetricsService metrics)
    {
        Activator = new ViewModelActivator();

        this.WhenActivated(disposables =>
        {
            metrics.Updates
                .Subscribe(UpdateMetrics, ReportFailure)
                .DisposeWith(disposables);

            Disposable
                .Create(StopTransientWork)
                .DisposeWith(disposables);
        });
    }

    public ViewModelActivator Activator { get; }
}
```

For the default distribution, import `ReactiveUI.Primitives.Disposables`; for `.Reactive`, use the matching System.Reactive disposable surface, including `System.Reactive.Disposables.Fluent` for `DisposeWith`. Do not mix disposable containers from sibling families unless an explicit adapter owns the boundary.

An `IViewFor<T>` view that calls `WhenActivated` participates in view activation and activates its `IActivatableViewModel`. A view-model activator alone does not infer visibility; the platform view/host must provide the activation signal.

## Reactive View

```csharp
public partial class DashboardView : ReactiveUserControl<DashboardViewModel>
{
    public DashboardView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(
                    ViewModel,
                    vm => vm.Status,
                    view => view.StatusText.Text)
                .DisposeWith(disposables);

            this.BindCommand(
                    ViewModel,
                    vm => vm.RefreshCommand,
                    view => view.RefreshButton)
                .DisposeWith(disposables);
        });
    }
}
```

Keep `WhenActivated` registration in the view constructor. Do not dispose the handle returned by `WhenActivated`; the platform activation infrastructure owns it. Dispose the work created inside each callback through the supplied scope.

## What Must Be Disposed

Dispose a subscription when either side can outlive the other or when work must stop before natural termination:

- View bindings and event streams, especially on dependency-property platforms.
- Subscriptions to singleton or long-lived services from a view model.
- `WhenAnyValue` chains that reference another object.
- `InvokeCommand`, DynamicData `Connect().Subscribe()`, interaction-handler registrations, timers, and hot sources.
- Command execution when deactivation should request cancellation.

Natural completion can own a finite one-shot subscription, but retaining it in the activation scope is still useful when deactivation should cancel or suppress its result. Disposal requests cleanup; it does not emit `OnCompleted`.

Self-owned constructor relationships can normally live for the view model's entire lifetime. A command and its OAPH owned by the same view model do not need activation solely to break a self-reference. Use activation when their upstream resource should pause while off-screen.

## Repeated Activation

Each activation callback can run multiple times. Prevent these defects:

| Defect | Correction |
| --- | --- |
| Duplicate command execution on every visual-tree attach | Make the trigger intentional or gate first-load state separately |
| Multiple interaction handlers after navigation | Dispose registrations with the activation scope |
| Recreating application services | Resolve services at composition; activate only their subscriptions |
| OAPH field replaced on every activation but declared readonly | Construct it once, or generate a writable helper only when activation-scoped replacement is required |
| Late async result updates inactive view | Cancel through command/token or observe only while active |

Activation is not initialization. Constructor invariants must hold before activation, and tests should be able to instantiate the view model without a platform lifecycle.

## Start and Stop Side Effects

Prefer a command or observable whose subscription owns cancellation:

```csharp
this.WhenActivated(disposables =>
{
    RefreshCommand
        .Execute()
        .Subscribe(_ => { }, ReportFailure)
        .DisposeWith(disposables);
});
```

For callback-style resources, pair acquisition and release in the same block with `Disposable.Create`, `CancellationDisposable`, or the family's replaceable/composite holder. Never rely on a finalizer for event detachment or UI resources.

Avoid `async void` activation callbacks. Model asynchronous setup as a command or observable and place its subscription in the activation scope. If the platform requires an async event handler, catch and route errors explicitly.

## Application-Lifetime Teardown

Activation does not replace deterministic object teardown. An application-lifetime view model or coordinator that owns asynchronous producers, commands, OAPHs, subscriptions, subjects, or platform resources should expose idempotent asynchronous disposal and be released by the composition root.

Teardown in ownership order:

1. Stop accepting new commands and request cancellation of active executions.
2. Serialize cleanup with operations that mutate the same resource.
3. Stop external producers, await consumer loops, and detach event handlers.
4. Dispose activation/session scopes, root subscriptions, OAPHs, and commands.
5. Complete subjects when completion is part of their contract; dispose owned primitives only after waiters and callbacks have exited.

Platform shutdown callbacks are often synchronous. When one must bridge to asynchronous disposal, make the disposal method idempotent, detach the callback, and catch or route failures inside the bridge; do not assume the platform awaits an `async void` handler.

## Lifecycle Tests

1. Activate the view model or view through the test helper/platform fixture.
2. Assert one subscription, handler, or initial action.
3. Deactivate and verify cancellation/unsubscription.
4. Emit after deactivation and verify no state change.
5. Reactivate and verify exactly one fresh subscription, not an accumulated duplicate.
6. Dispose the fixture and verify no callbacks remain.
7. Dispose during queued and active commands; verify cancellation reaches the underlying operation and teardown runs once.
