using System.Reactive.Linq;
using System.Windows.Input;

using Trace = System.Diagnostics.Trace;

namespace Stationary.Ftms.Dashboard;

public static class ReactiveInput
{
    public static readonly BindableProperty TextChangedCommandProperty = CreateCommandProperty(
        "TextChangedCommand",
        static view =>
        {
            var entry = (Entry)view;
            return Observable.FromEventPattern<TextChangedEventArgs>(
                    handler => entry.TextChanged += handler,
                    handler => entry.TextChanged -= handler)
                .Select(change => (object?)change.EventArgs.NewTextValue);
        });

    public static readonly BindableProperty ValueChangedCommandProperty = CreateCommandProperty(
        "ValueChangedCommand",
        static view =>
        {
            var slider = (Slider)view;
            return Observable.FromEventPattern<ValueChangedEventArgs>(
                    handler => slider.ValueChanged += handler,
                    handler => slider.ValueChanged -= handler)
                .Select(change => (object?)change.EventArgs.NewValue);
        });

    public static readonly BindableProperty SelectionChangedCommandProperty = CreateCommandProperty(
        "SelectionChangedCommand",
        static view =>
        {
            var collection = (CollectionView)view;
            return Observable.FromEventPattern<SelectionChangedEventArgs>(
                    handler => collection.SelectionChanged += handler,
                    handler => collection.SelectionChanged -= handler)
                .Select(change => change.EventArgs.CurrentSelection.FirstOrDefault());
        });

    public static readonly BindableProperty ToggledCommandProperty = CreateCommandProperty(
        "ToggledCommand",
        static view =>
        {
            var toggle = (Microsoft.Maui.Controls.Switch)view;
            return Observable.FromEventPattern<ToggledEventArgs>(
                    handler => toggle.Toggled += handler,
                    handler => toggle.Toggled -= handler)
                .Select(change => (object?)change.EventArgs.Value);
        });

    private static readonly BindableProperty SubscriptionsProperty = BindableProperty.CreateAttached(
        "Subscriptions", typeof(Dictionary<string, IDisposable>), typeof(ReactiveInput), null);

    public static ICommand? GetTextChangedCommand(BindableObject view) => (ICommand?)view.GetValue(TextChangedCommandProperty);
    public static void SetTextChangedCommand(BindableObject view, ICommand? command) => view.SetValue(TextChangedCommandProperty, command);
    public static ICommand? GetValueChangedCommand(BindableObject view) => (ICommand?)view.GetValue(ValueChangedCommandProperty);
    public static void SetValueChangedCommand(BindableObject view, ICommand? command) => view.SetValue(ValueChangedCommandProperty, command);
    public static ICommand? GetSelectionChangedCommand(BindableObject view) => (ICommand?)view.GetValue(SelectionChangedCommandProperty);
    public static void SetSelectionChangedCommand(BindableObject view, ICommand? command) => view.SetValue(SelectionChangedCommandProperty, command);
    public static ICommand? GetToggledCommand(BindableObject view) => (ICommand?)view.GetValue(ToggledCommandProperty);
    public static void SetToggledCommand(BindableObject view, ICommand? command) => view.SetValue(ToggledCommandProperty, command);

    internal static IObservable<T> WhileLoaded<T>(VisualElement view, Func<IObservable<T>> events) =>
        Observable.Merge(
                Observable.FromEventPattern(handler => view.Loaded += handler, handler => view.Loaded -= handler).Select(static _ => true),
                Observable.FromEventPattern(handler => view.Unloaded += handler, handler => view.Unloaded -= handler).Select(static _ => false))
            .StartWith(view.IsLoaded)
            .DistinctUntilChanged()
            .Select(loaded => loaded ? events() : Observable.Empty<T>())
            .Switch();

    private static BindableProperty CreateCommandProperty(string name, Func<VisualElement, IObservable<object?>> events) =>
        BindableProperty.CreateAttached(name, typeof(ICommand), typeof(ReactiveInput), null,
            propertyChanged: (bindable, _, value) =>
            {
                var view = (VisualElement)bindable;
                var subscriptions = (Dictionary<string, IDisposable>?)view.GetValue(SubscriptionsProperty);
                if (subscriptions is null)
                {
                    subscriptions = [];
                    view.SetValue(SubscriptionsProperty, subscriptions);
                }

                if (subscriptions.Remove(name, out var previous))
                {
                    previous.Dispose();
                }

                if (value is ICommand command)
                {
                    subscriptions[name] = WhileLoaded(view, () => events(view))
                        .Where(command.CanExecute)
                        .Subscribe(command.Execute, exception => Trace.TraceError("Input command {0} failed: {1}", name, exception));
                }
            });
}