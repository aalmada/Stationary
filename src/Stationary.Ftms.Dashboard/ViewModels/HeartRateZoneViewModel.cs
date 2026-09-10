using ReactiveUI;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.ViewModels;

public sealed class HeartRateZoneViewModel : ReactiveObject
{
    private string rangeText;
    private string timeInZoneText = "00:00:00";
    private bool isCurrent;

    public HeartRateZoneViewModel(HeartRateZone zone)
    {
        Code = zone.Code;
        Name = zone.Name;
        rangeText = GetRangeText(zone);
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

    public bool IsCurrent
    {
        get => isCurrent;
        private set => this.RaiseAndSetIfChanged(ref isCurrent, value);
    }

    internal void Update(HeartRateZoneDuration duration, bool current)
    {
        RangeText = GetRangeText(duration.Zone);
        TimeInZoneText = duration.Duration.ToString(@"hh\:mm\:ss");
        IsCurrent = current;
    }

    private static string GetRangeText(HeartRateZone zone) => zone.MaximumBeatsPerMinute is ushort maximum
        ? $"{zone.MinimumBeatsPerMinute}-{maximum} bpm"
        : $"{zone.MinimumBeatsPerMinute}+ bpm";
}