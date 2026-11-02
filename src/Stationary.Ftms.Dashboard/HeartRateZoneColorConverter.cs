using System.Globalization;

using Microsoft.Maui.Graphics;

using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

// Matches the zone palette of the 3D ride scene so the HUD ladder, the rings and the ECG share colours.
public sealed class HeartRateZoneColorConverter : IValueConverter
{
    private const int DefaultZoneCount = 5;
    private static readonly Color NoZone = Color.FromArgb("#8C40FF");
    private static readonly Color[] Palette =
    [
        Color.FromArgb("#99B3FF"),
        Color.FromArgb("#2680FF"),
        Color.FromArgb("#26FF73"),
        Color.FromArgb("#FF991A"),
        Color.FromArgb("#FF1F4D"),
    ];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RideSceneZone zone => GetColor(zone.Index, zone.Count),
        int index => GetColor(index, DefaultZoneCount),
        _ => NoZone,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Color GetColor(int index, int count) =>
        index < 0 || count <= 0 ? NoZone : Palette[int.Clamp(index * Palette.Length / count, 0, Palette.Length - 1)];
}