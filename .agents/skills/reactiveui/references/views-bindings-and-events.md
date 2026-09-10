# Views, Bindings, and Events

## View Contract

A reactive view implements `IViewFor<TViewModel>` and exposes a notifying `ViewModel` property. Prefer the platform's reactive base when available; use `[IViewFor<TViewModel>]` when source generation fits the control type.

| Platform | Typical base / implementation |
| --- | --- |
| WPF | `ReactiveWindow<T>` or `ReactiveUserControl<T>` |
| Avalonia | `ReactiveWindow<T>` or `ReactiveUserControl<T>` from `ReactiveUI.Avalonia` |
| MAUI | `ReactiveContentPage<T>` and related reactive page bases |
| WinUI 3 | `ReactivePage<T>` or `ReactiveUserControl<T>`; host in a plain `Window` |
| Windows Forms | `ReactiveUserControl<T>`; implement `IViewFor<T>` on `Form` |
| Blazor | `ReactiveComponentBase<T>` |

The view may know its view model and controls. The view model must not know the view or platform controls. Keep focus, animation, scrolling, visual conversion, and control-event adaptation in the view.

## Binding Selection

| API | Direction | Use |
| --- | --- | --- |
| `OneWayBind` | View model to view | Display derived/read-only state |
| `Bind` | Two-way | User-editable control value |
| `BindCommand` | Command to control event | Invoke a command and maintain enabled state |
| `BindTo` | Observable to target property | Flexible one-way view-specific transform |

Create every reactive binding in `WhenActivated` and dispose it with that activation scope:

```csharp
this.WhenActivated(disposables =>
{
    this.Bind(
            ViewModel,
            vm => vm.Query,
            view => view.QueryTextBox.Text)
        .DisposeWith(disposables);

    this.OneWayBind(
            ViewModel,
            vm => vm.IsBusy,
            view => view.BusyIndicator.IsVisible)
        .DisposeWith(disposables);

    this.BindCommand(
            ViewModel,
            vm => vm.SearchCommand,
            view => view.SearchButton)
        .DisposeWith(disposables);
});
```

Choose either platform/XAML binding or ReactiveUI binding for a property, not both. Duplicate owners produce feedback loops, duplicate command invocation, and confusing diagnostics.

## Conversion and Update Timing

Use a typed conversion delegate for a local one-way transform:

```csharp
this.OneWayBind(
        ViewModel,
        vm => vm.IsBusy,
        view => view.BusyPanel.Visibility,
        static busy => busy ? Visibility.Visible : Visibility.Collapsed)
    .DisposeWith(disposables);
```

Register an `IBindingTypeConverter` when the conversion recurs across views or must work in two directions. Keep domain conversion in the view model; keep platform types such as `Visibility`, colors, and images in the view/converter layer.

Two-way bindings update on target changes by default. Supply an update signal when changes should commit only on blur, submit, or another event. Ensure the signal is activated and disposed with the binding.

Prefer `BindTo` over a raw assignment subscription when ReactiveUI conversion and binding hooks matter. Use `Subscribe` for genuinely imperative view behavior such as focus or closing a window, and dispose it in the same activation block.

## Command Binding

`BindCommand` uses the platform's default control event or an explicitly selected event. It observes command executability and invokes the command through the binding infrastructure. Do not also attach a click handler that executes the same command.

Command parameters should represent the selected item or action input, not hidden view state. Bind editable state to the view model first and derive `CanExecute` there. Keep platform event arguments in the view unless they are part of the application contract.

## Observable Events

For current v24 projects, explicitly add the provider-aware `ReactiveUI.Primitives.ObservableEvents` analyzer when its version matches the installed Primitives family; it is not supplied merely by referencing ReactiveUI. It emits event observables for the default family, `.Reactive`, or standalone System.Reactive based on referenced symbols. `ReactiveMarbles.ObservableEvents.SourceGenerator` is the established alternative used by current platform guides. Do not add obsolete `ReactiveUI.Events.*` packages.

```csharp
this.WhenActivated(disposables =>
{
    SearchBox.Events().KeyUp
        .Subscribe(args => HandleKey(args.Key))
        .DisposeWith(disposables);
});
```

If no generator supports the event, use the chosen observable family's `FromEvent`/`FromEventPattern` with symmetric add/remove handlers. Attach and detach on the UI scheduler when the platform requires UI-thread event access.

Do not defer paint/draw event arguments through `ObserveOn` or `await` when their graphics context is valid only during the callback. Draw synchronously, or schedule an invalidation and render during the next paint callback.

## View Location and Templates

`ViewModelViewHost` resolves and displays an `IViewFor<T>` for its `ViewModel`. Desktop XAML item controls can receive an automatic host template when neither `ItemTemplate` nor `DisplayMemberPath` is set. MAUI does not support that automatic item-template behavior; provide a `DataTemplate` explicitly.

Use a contract when one view-model type has multiple views, such as compact and detailed presentations. Register every contract explicitly and pass the same contract to resolution/hosting.

For trimmed/AOT applications, register views explicitly or through source-generated registration. Reflection scanning through `WithViewsFromAssembly` is convenient but marked `RequiresUnreferencedCode`.

## Blazor

Current ReactiveUI 24 `ReactiveComponentBase<T>` wires view-model `PropertyChanged` notifications to rendering and manages activation. Older guides show manual subscriptions that call `InvokeAsync(StateHasChanged)` for each property; inspect the installed base before adding duplicate rerender subscriptions. Custom component bases still need to marshal rendering through `InvokeAsync`.

## Binding Diagnostics

- Configure a Splat logger during startup; unsupported property notification and failed binding conversion are otherwise easy to miss.
- Treat a runtime warning about a POCO property as a broken reactive contract, not harmless noise.
- Verify null `ViewModel` transitions and design-time creation paths.
- Test conversion functions separately when they contain branching or culture-sensitive parsing.
- On leaks, inspect activation, handler disposal, long-lived services, dependency properties, and cached views before blaming the binding engine.
