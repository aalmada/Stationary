namespace Stationary.Ftms.Dashboard.Core;

public sealed record HeartRateZone
{
    public HeartRateZone(string code, string name, ushort minimumBeatsPerMinute, ushort? maximumBeatsPerMinute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (maximumBeatsPerMinute is ushort maximum && maximum < minimumBeatsPerMinute)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBeatsPerMinute));
        }

        Code = code;
        Name = name;
        MinimumBeatsPerMinute = minimumBeatsPerMinute;
        MaximumBeatsPerMinute = maximumBeatsPerMinute;
    }

    public string Code { get; }

    public string Name { get; }

    public ushort MinimumBeatsPerMinute { get; }

    public ushort? MaximumBeatsPerMinute { get; }

    public bool Contains(ushort beatsPerMinute) => beatsPerMinute >= MinimumBeatsPerMinute
        && (MaximumBeatsPerMinute is not ushort maximum || beatsPerMinute <= maximum);
}