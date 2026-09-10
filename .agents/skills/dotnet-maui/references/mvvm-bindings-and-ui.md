# MVVM, Bindings, And UI

## View And View Model Boundaries

- Use the view for layout, visual states, bindings, and accessibility metadata. Keep application actions in commands and presentation state in the view model.
- Bind the injected view model in the page constructor or through an established locator pattern. Do not put network, persistence, or domain rules in code-behind.
- Use `INotifyPropertyChanged` and `ICommand`, or an established MVVM toolkit, for bindable state and actions. Keep command `CanExecute` state accurate.

## Binding Rules

- Set `x:DataType` on pages, layouts, data templates, and controls where practical to enable compiled bindings and compile-time binding checks.
- Bind UI state declaratively instead of manually synchronizing controls in event handlers.
- MAUI marshals `INotifyPropertyChanged.PropertyChanged` binding notifications to the UI thread. It does not marshal `ObservableCollection<T>.CollectionChanged`; dispatch collection mutations to the main thread.
- Use `MainThread.BeginInvokeOnMainThread` for native UI access and bound collection mutation initiated by background work.

## Layout And Resources

- Use `CollectionView` for lists. Keep templates shallow and avoid blocking I/O, costly converters, or repeated image decoding in binding paths.
- Centralize colors, typography, spacing, and reusable control styles in resource dictionaries. Use `DynamicResource` only when a value must change at runtime, such as theme switching.
- Prefer `VisualStateManager` for state-dependent styling over imperative property changes.
- Test font scaling, light/dark themes, orientation, window resize, localization expansion, keyboard navigation, and screen-reader output.

## Accessibility And Localization

- Supply semantic descriptions for meaningful controls, correct control types, visible focus behavior, and a logical keyboard order.
- Use localized resource files and avoid concatenating localized fragments. Format dates, numbers, and currency with culture-aware APIs.
- Do not use color, position, or gesture as the only indication of state or action.

## Sources

- [Data binding](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/?view=net-maui-10.0)
- [Compiled bindings](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/compiled-bindings?view=net-maui-10.0)
- [Accessibility](https://learn.microsoft.com/dotnet/maui/fundamentals/accessibility?view=net-maui-10.0)
