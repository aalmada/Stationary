using Microsoft.Maui.Graphics;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.ViewModels;

public sealed partial class HeartRateZoneViewModel : ReactiveObject
{
    public HeartRateZoneViewModel(HeartRateZone zone)
    {
        Code = zone.Code;
        Name = zone.Name;
        RangeText = GetRangeText(zone);
        ZoneColor = GetZoneColor(zone.Code);
    }

    public string Code { get; }

    public string Name { get; }

    [Reactive]
    public partial string RangeText { get; private set; }

    [Reactive]
    public partial string TimeInZoneText { get; private set; } = "00:00:00";

    [Reactive]
    public partial string PercentageText { get; private set; } = "0%";

    public Color ZoneColor { get; }

    [Reactive]
    public partial bool IsCurrent { get; private set; }

    internal void Update(HeartRateZoneDuration duration, TimeSpan totalDuration, bool current)
    {
        RangeText = GetRangeText(duration.Zone);
        TimeInZoneText = duration.Duration.ToString(@"hh\:mm\:ss");
        PercentageText = totalDuration > TimeSpan.Zero
            ? $"{duration.Duration / totalDuration:P0}"
            : "0%";
        IsCurrent = current;
    }

    private static Color GetZoneColor(string code) => code switch
    {
        "Z1" => Color.FromArgb("86A5C4"),
        "Z2" => Color.FromArgb("4A90E2"),
        "Z3" => Color.FromArgb("3C9D67"),
        "Z4" => Color.FromArgb("E0B332"),
        "Z5a" => Color.FromArgb("E67E22"),
        "Z5b" => Color.FromArgb("D94A4A"),
        "Z5c" => Color.FromArgb("A6469B"),
        _ => Colors.Gray,
    };

    private static string GetRangeText(HeartRateZone zone) => zone.MaximumBeatsPerMinute is ushort maximum
        ? $"{zone.MinimumBeatsPerMinute}-{maximum} bpm"
        : $"{zone.MinimumBeatsPerMinute}+ bpm";
}