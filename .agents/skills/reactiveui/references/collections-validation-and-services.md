# Collections, Validation, and Services

## DynamicData

Use DynamicData for live collections that need filtering, sorting, grouping, transformation, paging, expiration, or item-property refresh. An `ObservableCollection<T>` is adequate for a small UI-thread-only list with no derived query; it is not a replacement for a change-set pipeline.

| Source | Choose when |
| --- | --- |
| `SourceCache<TObject, TKey>` | Items have a stable unique key; prefer this for updates and richer operators |
| `SourceList<T>` | Duplicates and positional semantics are required |
| Existing `ObservableCollection<T>` plus `ToObservableChangeSet` | Adapting a simple UI-thread-owned collection incrementally |

Keep mutable sources private. Expose `IObservable<IChangeSet<...>>`, `IObservableCache`, `IObservableList`, or a bound `ReadOnlyObservableCollection<T>` according to the consumer's needs. Do not expose `SourceCache`/`SourceList` mutation APIs broadly.

```csharp
private readonly SourceCache<Item, Guid> _source = new(item => item.Id);
private readonly ReadOnlyObservableCollection<ItemRowViewModel> _items;
private readonly IDisposable _connection;

public ReadOnlyObservableCollection<ItemRowViewModel> Items => _items;

public ItemsViewModel()
{
    _connection = _source.Connect()
        .AutoRefresh(item => item.IsVisible)
        .Filter(item => item.IsVisible)
        .Transform(item => new ItemRowViewModel(item))
        .Sort(SortExpressionComparer<ItemRowViewModel>.Ascending(item => item.Name))
        .ObserveOn(RxSchedulers.MainThreadScheduler)
        .Bind(out _items)
        .DisposeMany()
        .SubscribePrimitives();
}
```

Apply filtering, transformation, sorting, and aggregation before `ObserveOn`; marshal to the UI immediately before `Bind`. `ObserveOn` near the source moves all work onto the UI thread and defeats background processing.

Use `Edit` to batch related source mutations into one change set. Use `AutoRefresh(property)` only for item properties that affect the query; observing every property creates unnecessary work. Use `DisposeMany` when a transform creates disposable child view models.

`ToCollection()` materializes the entire current set on every change. Reserve it for aggregates that require a snapshot. `Bind` applies incremental changes and is the normal UI projection.

Bound every long-lived cache: constrain `ToObservableChangeSet` with size/expiry when fed by an unbounded stream, and dispose the retained connection and mutable source with their owner. DynamicData depends on System.Reactive; in a default-family ReactiveUI app, isolate imports when Primitives and System.Reactive expose overlapping extension names. A parameterless `.Subscribe()` is ambiguous in this mixed graph; use `SubscribePrimitives()` to select the default-family implementation, as the example does.

## Validation

Use a simple `IObservable<bool>` as `CanExecute` when only command availability matters. When a compatible ReactiveUI.Validation version is resolved, use it for per-property messages, form-level state, asynchronous rules, localization, or `INotifyDataErrorInfo` integration. The following pattern is its API shape, not a compatible ReactiveUI 24.2.0 example:

```csharp
using ReactiveUI.Validation.Contexts;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

public sealed class AccountViewModel : ReactiveValidationObject
{
    public AccountViewModel()
    {
        this.ValidationRule(
            viewModel => viewModel.Email,
            email => !string.IsNullOrWhiteSpace(email) && email.Contains('@'),
            "Enter a valid email address.");
    }

    public string Email
    {
        get => _email;
        set => this.RaiseAndSetIfChanged(ref _email, value);
    }

    private string _email = string.Empty;
}
```

`ReactiveValidationObject` implements `IValidatableViewModel` and `INotifyDataErrorInfo`. A custom base can implement `IValidatableViewModel` and expose a `ValidationContext` instead.

For cross-property or async rules, supply an observable state stream and identify the target property when `INotifyDataErrorInfo` must associate the message. Cancel or switch away from stale async validation so an older response cannot overwrite a newer input state.

Bind presentation in the active view:

```csharp
this.BindValidation(
        ViewModel,
        vm => vm.Email,
        view => view.EmailError.Text)
    .DisposeWith(disposables);
```

Use custom `IValidationTextFormatter<T>` implementations for localization or platform formatting. Keep message keys/domain rules in the view model and visual rendering in the view.

Verified baseline: ReactiveUI.Validation 7.1.0 restores beside ReactiveUI 24.2.0 but fails compilation because its validation extensions cannot resolve the v24 `IReactiveObject` contract. No `ReactiveUI.Validation.Reactive` sibling is published. Do not use this version pair. Resolve a release that explicitly supports the selected ReactiveUI distribution, or choose another validation library; a successful NuGet restore is not compatibility proof.

## Dependency Injection

- Configure Splat or a supported external-container adapter once through `RxAppBuilder`.
- Prefer constructor injection for services, schedulers, and factories used by view models.
- Use AppLocator only at framework-required registration/resolution boundaries and during incremental migration.
- Do not resolve required dependencies with `GetService<T>() ?? new ...`; it hides composition errors and creates inconsistent lifetimes.
- Keep platform services behind shared interfaces. Register implementations in each platform head.
- Avoid injecting a general service provider or locator into view models.

## Logging and Fatal Errors

Splat uses a null logger unless an `ILogger` implementation or adapter is registered. Configure logging at startup so binding failures, unsupported property notifications, and framework diagnostics are visible. Prefer the existing application logging stack through a Splat adapter.

Classes using Splat logging implement `IEnableLogger` and call `this.Log()`. Observable `Log`/`LoggedCatch` helpers are diagnostic/recovery operators and still require subscription. Do not add logging operators throughout hot paths permanently without measuring their cost.

Configure the global last-resort exception observer through `RxAppBuilder.WithExceptionHandler`. Keep it fatal or telemetry-oriented. Expected I/O and domain failures still need local command/pipeline handling; a global observer cannot restore command or view-model state.

## Message Bus

Prefer explicit services, commands, state observables, and routing. Use `MessageBus` only for genuinely decoupled application-wide notifications where sender and receiver should not share a narrower contract. Name and schedule message contracts, dispose subscriptions, and isolate the global bus in tests. A message bus is not a substitute for dependency injection or a state store.

## Review Checks

- Collection source ownership, connection disposal, update thread, and UI handoff are explicit.
- Cache/list choice matches key and duplicate semantics.
- Materialization, replay, expiration, sorting, and item refresh cannot grow unnoticed.
- Validation distinguishes invalid input, pending async work, service failure, and programmer error.
- ReactiveUI.Validation is compatible with the chosen distribution and target frameworks.
- Services use constructor injection and platform boundaries; locator access is isolated.
- Logging and the global exception observer supplement, rather than replace, local error handling.
