using System.Windows.Input;

namespace Stationary.Ftms.Dashboard;

// An in-page tab strip rendered by the platform's native segmented control, so it has tab semantics and keyboard
// focus. The selection is one-way from the view model; a user choice is routed through SelectionChangedCommand.
public sealed class SegmentedTabs : View
{
    public static readonly BindableProperty TitlesProperty = BindableProperty.Create(
        nameof(Titles),
        typeof(IReadOnlyList<string>),
        typeof(SegmentedTabs),
        Array.Empty<string>());

    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
        nameof(SelectedIndex),
        typeof(int),
        typeof(SegmentedTabs),
        0);

    public static readonly BindableProperty SelectionChangedCommandProperty = BindableProperty.Create(
        nameof(SelectionChangedCommand),
        typeof(ICommand),
        typeof(SegmentedTabs));

    public static readonly BindableProperty SelectedColorProperty = BindableProperty.Create(
        nameof(SelectedColor),
        typeof(Color),
        typeof(SegmentedTabs));

    public IReadOnlyList<string> Titles
    {
        get => (IReadOnlyList<string>)GetValue(TitlesProperty);
        set => SetValue(TitlesProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public ICommand? SelectionChangedCommand
    {
        get => (ICommand?)GetValue(SelectionChangedCommandProperty);
        set => SetValue(SelectionChangedCommandProperty, value);
    }

    public Color? SelectedColor
    {
        get => (Color?)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    // Returns false when the command rejects the choice, so the handler restores the bound selection.
    internal bool RequestSelection(int index)
    {
        if (SelectionChangedCommand is { } command && command.CanExecute(index))
        {
            command.Execute(index);
            return true;
        }

        return false;
    }
}