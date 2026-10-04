using System.Globalization;

namespace Stationary.Ftms.Dashboard;

public sealed class TrainingDurationWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TimeSpan duration && duration > TimeSpan.Zero
            ? (float)duration.TotalSeconds
            : 0f;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}