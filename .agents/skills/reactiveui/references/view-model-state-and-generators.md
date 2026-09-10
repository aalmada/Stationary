# View-Model State and Source Generators

## State Categories

| State | Model it as |
| --- | --- |
| User-editable value | Reactive property with change notification |
| Constructor-only dependency/value | Ordinary get-only property or field |
| Value derived from other state | Observable pipeline exposed through OAPH |
| User action | `ReactiveCommand`, not a boolean trigger property |
| UI-only state such as focus or scroll position | View code, not the view model |
| Dialog answer or other UI response | `Interaction<TInput, TOutput>` |

Inherit from `ReactiveObject` unless another base class is mandatory; in that case implement `IReactiveObject` or use the source generator's `[IReactiveObject]`. `ReactiveObject` provides `INotifyPropertyChanging`, `INotifyPropertyChanged`, and observable `Changing`/`Changed` streams.

## Writable Properties

Prefer ReactiveUI.SourceGenerators in new code:

```csharp
using ReactiveUI;
using ReactiveUI.SourceGenerators;

public partial class ProfileViewModel : ReactiveObject
{
    [Reactive]
    private string _firstName = string.Empty;
}
```

The manual equivalent remains valid:

```csharp
private string _firstName = string.Empty;

public string FirstName
{
    get => _firstName;
    set => this.RaiseAndSetIfChanged(ref _firstName, value);
}
```

Keep setters declarative and side-effect free. Observe the property with `WhenAnyValue` for validation, commands, persistence, or derived state. `RaiseAndSetIfChanged` suppresses equal assignments according to the property's equality behavior.

## `WhenAny` Family

| API | Use |
| --- | --- |
| `WhenAnyValue` | Observe current and future property values; default choice |
| `WhenAny` | Also require sender or expression metadata through `IObservedChange` |
| `WhenAnyObservable` | Follow an observable-valued property and switch when the owning property changes |

`WhenAnyValue` emits the current value on subscription and then final-value changes. Every member in the expression chain needs a supported notification mechanism. A plain POCO member without notifications yields only the value available at subscription and logs a warning when logging is configured.

For `x => x.Parent.Child.Value`, a null intermediate suppresses a value rather than emitting a `NullReferenceException`; observation resumes when the chain is evaluable. Replacing an intermediate object with another object that yields the same final value does not produce a distinct final-value notification. Split nullable chains into explicit observations when null itself is meaningful state.

## Derived State with OAPH

Use `ObservableAsPropertyHelper<T>` for read-only state owned by an observable:

```csharp
public partial class ProfileViewModel : ReactiveObject
{
    [Reactive]
    private string _firstName = string.Empty;

    [Reactive]
    private string _lastName = string.Empty;

    [ObservableAsProperty]
    private string _displayName = string.Empty;

    public ProfileViewModel()
    {
        _displayNameHelper = this
            .WhenAnyValue(x => x.FirstName, x => x.LastName,
                static (first, last) => $"{first} {last}".Trim())
            .ToProperty(this, nameof(DisplayName), initialValue: string.Empty);
    }
}
```

In this field-backed generator form, `[ObservableAsProperty]` generates both `DisplayName` and `_displayNameHelper`; it cannot invent the source observable, so the constructor assigns that helper. Observable method/property forms can instead use the generator's `InitializeOAPH()` path.

OAPH rules:

- `ToProperty` subscribes to the source and raises property notifications; treat it as a lifetime-owning subscription.
- Its default notification context is current-thread scheduling, not automatically the UI scheduler. Pass `scheduler: RxSchedulers.MainThreadScheduler` when upstream can emit off-thread and a UI observes the property.
- Set `initialValue` when `default(T)` is not a valid pre-emission state.
- `deferSubscription: true` starts on first property access. A deferred OAPH can miss values from a hot source; replay/share upstream when the latest value must survive.
- An upstream `OnError` terminates the OAPH subscription. Convert recoverable failures to state before `ToProperty`; do not expect the property helper to recover.
- Prefer the `nameof` overload on hot construction paths when expression metadata is unnecessary.

Do not subscribe merely to assign a second property. A `WhenAnyValue(...).Select(...).ToProperty(...)` relationship preserves ownership, testability, and a single writer.

## Generator Surface

ReactiveUI.SourceGenerators 3.2 detects the selected ReactiveUI 24 distribution automatically. Add it as a private analyzer dependency and make generated types `partial`.

Place generated types in a named namespace. Version 3.2.0 emits invalid generated namespace syntax for a type declared in the global namespace; repository examples should not rely on global-namespace snippets compiling unchanged.

| Attribute | Generates / configures |
| --- | --- |
| `[Reactive]` | Writable property calling `RaiseAndSetIfChanged` |
| `[ObservableAsProperty]` | Read-only property and OAPH helper field |
| `[ReactiveCommand]` | Command property from a method |
| `[IViewFor<TViewModel>]` | Typed `IViewFor` plumbing and optional Splat registration |
| `[IReactiveObject]` | Reactive-object implementation when inheritance is unavailable |
| `[ReactiveCollection]` | Collection-property notifications for an `ObservableCollection` field |
| `[BindableDerivedList]` | Bindable property for a derived `ReadOnlyObservableCollection` |

Field-backed declarations work with C# 12. Partial-property declarations require C# 13; partial-property initializers require the documented C# 14/preview compiler support. Use field-backed declarations when the repository's compiler baseline is lower.

`[ReactiveCommand]` accepts task methods, observable methods, optional input, return values, and a final `CancellationToken`. It strips an `Async` suffix when naming the generated command. `RunInBackground = true` applies to synchronous methods and selects `CreateRunInBackground`; it does not replace naturally asynchronous task APIs.

Source generators run from the same original compilation and cannot consume members emitted by another generator in that round. If a JSON source-generation context must discover ReactiveUI-generated properties, place the generated model in one assembly and the serializer context in a referencing assembly, or declare the serialized surface manually.

## Review Checks

- Every mutable property raises notifications exactly once per meaningful change.
- Setters do not launch I/O, mutate unrelated state, or block.
- Derived values have one observable writer and an explicit initial/error policy.
- Every property path used by `WhenAnyValue` supports notifications.
- Generator package and language version support every attribute shape in use.
- Generated helper names are confirmed from compiler output before hand-written code references them.
- UI types and service-locator calls do not leak into reusable view models.
