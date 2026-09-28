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

## Styles And Visual States

- Put global styles in `Application.Resources`. Omit `x:Key` for an implicit style that applies to every exact `TargetType` in scope; use a keyed explicit style for opt-in variants.
- Define `Normal`, `Disabled`, `Focused`, and `PointerOver` appearances with `VisualStateManager` in XAML. Each visual state styles the element whose state changes.
- Style the element that paints the visible surface. A disabled child `Button` does not put its parent `Border` into the `Disabled` state. If a wrapper owns the background or stroke, bind or trigger that wrapper from the child's `IsEnabled`, or move the surface styling onto the button.
- Include every property that must differ in a state. For a disabled action, set its background or `Background` brush, text color, border or stroke, and opacity explicitly when platform defaults could remain visible.
- Prefer XAML styles for complete and stateful styling. MAUI CSS is runtime-parsed, cannot fully style an app, does not support CSS variables, and does not support `:` or `::` selectors such as `:disabled`.
- Do not expect CSS to select a parent from child state. Use XAML bindings, triggers, visual states, or a reusable control instead.

## Accessibility And Localization

- Supply semantic descriptions for meaningful controls, correct control types, visible focus behavior, and a logical keyboard order.
- Use localized resource files and avoid concatenating localized fragments. Format dates, numbers, and currency with culture-aware APIs.
- Do not use color, position, or gesture as the only indication of state or action.

## Sources

- [Data binding](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/?view=net-maui-10.0)
- [Compiled bindings](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/compiled-bindings?view=net-maui-10.0)
- [XAML styles](https://learn.microsoft.com/dotnet/maui/user-interface/styles/xaml?view=net-maui-10.0)
- [CSS styles](https://learn.microsoft.com/dotnet/maui/user-interface/styles/css?view=net-maui-10.0)
- [Visual states](https://learn.microsoft.com/dotnet/maui/user-interface/visual-states?view=net-maui-10.0)
- [Accessibility](https://learn.microsoft.com/dotnet/maui/fundamentals/accessibility?view=net-maui-10.0)
