using System.Globalization;

using Microsoft.Maui.Graphics;

namespace Stationary.Ftms.Dashboard;

public sealed class TrainingDurationColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TimeSpan duration
            ? duration.TotalMinutes switch
            {
                <= 35d => Color.FromArgb("#C5A23C"),
                <= 50d => Color.FromArgb("#0C8F80"),
                _ => Color.FromArgb("#285A78"),
            }
            : Color.FromArgb("#0C8F80");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}