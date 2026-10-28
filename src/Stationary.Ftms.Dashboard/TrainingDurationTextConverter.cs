using System.Globalization;

namespace Stationary.Ftms.Dashboard;

public sealed class TrainingDurationTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan duration || duration <= TimeSpan.Zero)
        {
            return "0 min";
        }

        if (duration.TotalHours >= 1d)
        {
            return $"{(int)duration.TotalHours} h {duration.Minutes:D2} min";
        }

        return $"{(int)double.Ceiling(duration.TotalMinutes)} min";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}