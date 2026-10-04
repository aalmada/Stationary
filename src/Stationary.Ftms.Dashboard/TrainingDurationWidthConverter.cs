using System.Globalization;

namespace Stationary.Ftms.Dashboard;

public sealed class TrainingDurationWidthConverter : IValueConverter
{
    private const double PixelsPerMinute = 2d;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TimeSpan duration && duration > TimeSpan.Zero
            ? duration.TotalMinutes * PixelsPerMinute
            : 0d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}