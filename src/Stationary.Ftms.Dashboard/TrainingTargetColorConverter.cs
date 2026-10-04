using System.Globalization;

using Microsoft.Maui.Graphics;

namespace Stationary.Ftms.Dashboard;

public sealed class TrainingTargetColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double percentage
            ? percentage switch
            {
                <= 60d => Color.FromArgb("#147D70"),
                <= 75d => Color.FromArgb("#46A36A"),
                <= 90d => Color.FromArgb("#C5A23C"),
                <= 105d => Color.FromArgb("#E17D3D"),
                <= 120d => Color.FromArgb("#D9564D"),
                _ => Color.FromArgb("#B63862"),
            }
            : Color.FromArgb("#147D70");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}