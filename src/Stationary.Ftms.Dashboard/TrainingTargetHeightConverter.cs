using System.Globalization;

namespace Stationary.Ftms.Dashboard;

public sealed class TrainingTargetHeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double percentage
            ? double.Clamp(percentage * 0.28d, 12d, 42d)
            : 12d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}