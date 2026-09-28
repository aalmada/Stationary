using ReactiveUI;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.ViewModels;

public sealed class HeartRateZoneViewModel : ReactiveObject
{
    private string rangeText;
    private string timeInZoneText = "00:00:00";
    private string percentageText = "0%";
    private bool isCurrent;

    public HeartRateZoneViewModel(HeartRateZone zone)
    {
        Code = zone.Code;
        Name = zone.Name;
        rangeText = GetRangeText(zone);
        ZoneColor = GetZoneColor(zone.Code);
    }

    public string Code { get; }

    public string Name { get; }

    public string RangeText
    {
        get => rangeText;
        private set => this.RaiseAndSetIfChanged(ref rangeText, value);
    }

    public string TimeInZoneText
    {
        get => timeInZoneText;
        private set => this.RaiseAndSetIfChanged(ref timeInZoneText, value);
    }

    public string PercentageText
    {
        get => percentageText;
        private set => this.RaiseAndSetIfChanged(ref percentageText, value);
    }

    public Color ZoneColor { get; }

    public bool IsCurrent
    {
        get => isCurrent;
        private set => this.RaiseAndSetIfChanged(ref isCurrent, value);
    }

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