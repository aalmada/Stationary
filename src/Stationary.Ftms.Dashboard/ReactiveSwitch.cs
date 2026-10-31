using System.Windows.Input;

namespace Stationary.Ftms.Dashboard;

public sealed class ReactiveSwitch : Switch
{
    public static readonly BindableProperty ToggledCommandProperty = BindableProperty.Create(
        nameof(ToggledCommand),
        typeof(ICommand),
        typeof(ReactiveSwitch),
        propertyChanged: static (bindable, _, value) => ReactiveInput.SetToggledCommand(bindable, (ICommand?)value));

    public ICommand? ToggledCommand
    {
        get => (ICommand?)GetValue(ToggledCommandProperty);
        set => SetValue(ToggledCommandProperty, value);
    }

}