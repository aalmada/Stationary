# Interactions, Routing, and View Location

## Interactions

Use `Interaction<TInput, TOutput>` when a view model needs a UI response without depending on a dialog, page, or control. The view model owns the interaction; the active view owns its handler.

View model:

```csharp
public Interaction<string, bool> ConfirmDelete { get; } = new();

private async Task DeleteAsync(string fileName)
{
    bool confirmed = await ConfirmDelete.Handle(fileName);
    if (confirmed)
    {
        await DeleteFileAsync(fileName);
    }
}
```

View:

```csharp
this.WhenActivated(disposables =>
{
    ViewModel!.ConfirmDelete
        .RegisterHandler(async context =>
        {
            bool confirmed = await ShowConfirmationAsync(context.Input);
            context.SetOutput(confirmed);
        })
        .DisposeWith(disposables);
});
```

`Handle` is lazy; await or subscribe to invoke handlers. A handler must call `SetOutput` to claim the interaction. If no registered handler supplies output, `Handle` fails with `UnhandledInteractionException<TInput, TOutput>`.

Registrations form a last-registered-first handler chain. A handler may decline by not setting output, allowing an earlier handler to run. This supports an application default plus a temporary active-view override, provided every temporary registration is disposed.

Use interactions for modal prompts, file pickers, permissions, and other request/response UI. Use ordinary observable state for passive notifications and routing for durable navigation state.

## Interaction Tests

```csharp
using IDisposable registration = viewModel.ConfirmDelete.RegisterHandler(
    context => context.SetOutput(true));

await viewModel.DeleteCommand.Execute(fileName);
```

Test affirmative, negative, unhandled, and handler-precedence cases. Dispose shared interaction registrations before the test ends. Do not mock the view or show a real dialog.

## Routing Model

ReactiveUI routing is view-model-first navigation:

| Type | Responsibility |
| --- | --- |
| `IScreen` | Owns one `RoutingState` |
| `RoutingState` | Owns the navigation stack and navigation commands |
| `IRoutableViewModel` | Supplies `UrlPathSegment` and `HostScreen` |
| `RoutedViewHost` | Observes the router and displays the located view |
| `IViewLocator` | Maps a view-model instance and optional contract to an `IViewFor` |

Core routing types remain in the main ReactiveUI package in v24. `ReactiveUI.Routing` contains additional DynamicData change-set routing, collection, and auto-persist helpers; choose its matching distribution only when those extensions are needed.

```csharp
public sealed class ShellViewModel : ReactiveObject, IScreen
{
    public RoutingState Router { get; } = new();

    public IObservable<IRoutableViewModel> ShowDetails(Item item) =>
        Router.Navigate.Execute(new DetailsViewModel(this, item));
}

public sealed class DetailsViewModel : ReactiveObject, IRoutableViewModel
{
    public DetailsViewModel(IScreen hostScreen, Item item)
    {
        HostScreen = hostScreen;
        Item = item;
    }

    public string UrlPathSegment => $"items/{Item.Id}";
    public IScreen HostScreen { get; }
    public Item Item { get; }
}
```

Navigation commands are reactive and lazy. Subscribe or await `Navigate.Execute`, `NavigateBack.Execute`, or the selected reset operation. Construct the destination view model with its required application data rather than using the URL segment as an implicit service locator.

Use one `IScreen` per navigation region. Nested screens and routed hosts are valid; define which router owns Back behavior. Use `CanExecute` from the router command rather than duplicating stack-count logic unless the application adds stricter policy.

Routing is not required. Prefer the platform router when it owns deep links, browser history, MAUI Shell routes, native transitions, or restoration more naturally. Wrap platform navigation behind a view-model-facing service or command instead of forcing `RoutingState` into every app.

## View Registration

Register a view as `IViewFor<TViewModel>` using one of these paths:

1. Explicit builder registration, preferred for trimming/AOT and reviewability.
2. `[IViewFor<TViewModel>]` plus source-generated registration.
3. `WithViewsFromAssembly` or `RegisterViewsForViewModels` reflection scanning when dynamic-code constraints allow it.
4. A custom `IViewLocator` when mapping depends on conventions, plugins, or runtime state.

Prefer transient views. A singleton view retains controls, activation state, and platform resources; use it only when the host and navigation model guarantee one lifetime.

Contracts distinguish multiple views for the same view model. Keep contract values centralized, register the `(view model, contract)` pair, and test both successful resolution and the missing-view failure path.

## Custom View Locator

```csharp
public sealed class AppViewLocator : IViewLocator
{
    public IViewFor<TViewModel>? ResolveView<TViewModel>()
        where TViewModel : class => ResolveView<TViewModel>(null);

    public IViewFor<TViewModel>? ResolveView<TViewModel>(string? contract)
        where TViewModel : class =>
        typeof(TViewModel) == typeof(DetailsViewModel)
            ? (IViewFor<TViewModel>)(object)(contract == "compact"
                ? new CompactDetailsView()
                : new DetailsView())
            : null;

    public IViewFor? ResolveView(object? viewModel) => ResolveView(viewModel, null);

    public IViewFor? ResolveView(object? viewModel, string? contract) =>
        (viewModel, contract) switch
        {
            (DetailsViewModel details, "compact") =>
                new CompactDetailsView { ViewModel = details },
            (DetailsViewModel details, _) =>
                new DetailsView { ViewModel = details },
            _ => null,
        };
}
```

ReactiveUI 24 uses four locator overloads: generic resolution by view-model type, with and without a contract, plus object-instance resolution, with and without a contract. Older handbook examples implement a single generic method that accepts the instance and no longer satisfy this interface. A custom locator should return a fresh correctly typed view or an intentional null/failure, not swallow construction exceptions. Register it after platform/core services so it replaces the default locator.

## Navigation and Activation

- Routed views should set up bindings and interaction handlers in `WhenActivated`.
- Routable view models can use activation for subscriptions that should run only while present.
- Popping a route must deactivate and release its view-owned resources; test navigation cycles for retained handlers.
- Use interactions for modal/pop-up responses. A modal question is not durable navigation-stack state.
- Keep persistence explicit. A URL segment names a route; it is not sufficient serialization for arbitrary view-model state.

## Review Checks

- Every `Handle` and navigation `Execute` is awaited or subscribed.
- Every view handler registration is activation-scoped.
- The router, host screen, and routed host belong to the intended navigation region.
- Every routable view model resolves to a registered `IViewFor` for each used contract.
- Reflection registration is absent from trimmed/AOT paths or protected by linker configuration verified in publish tests.
- Back, reset, duplicate-route, failed-construction, and reactivation behavior have tests.
