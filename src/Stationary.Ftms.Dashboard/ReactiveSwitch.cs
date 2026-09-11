using System.Windows.Input;

namespace Stationary.Ftms.Dashboard;

public sealed class ReactiveSwitch : Switch
{
    public static readonly BindableProperty ToggledCommandProperty = BindableProperty.Create(
        nameof(ToggledCommand),
        typeof(ICommand),
        typeof(ReactiveSwitch));

    public ReactiveSwitch()
    {
        Toggled += HandleToggled;
    }

    public ICommand? ToggledCommand
    {
        get => (ICommand?)GetValue(ToggledCommandProperty);
        set => SetValue(ToggledCommandProperty, value);
    }

    private void HandleToggled(object? sender, ToggledEventArgs eventArgs)
    {
        if (ToggledCommand?.CanExecute(eventArgs.Value) == true)
        {
            ToggledCommand.Execute(eventArgs.Value);
        }
    }
}