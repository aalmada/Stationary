namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct HeartRateZoneDuration(HeartRateZone Zone, TimeSpan Duration);

public sealed class HeartRateSessionAnalytics
{
    private readonly long[] zoneDurationTicks;
    private HeartRateObservation? lastObservation;
    private long totalBeatsPerMinute;
    private int measurementCount;
    private ushort? maximumBeatsPerMinute;
    private bool isPaused;

    public HeartRateSessionAnalytics(HeartRateZoneProfile profile, TimeSpan maximumGap)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (maximumGap <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumGap));
        }

        Profile = profile;
        MaximumGap = maximumGap;
        zoneDurationTicks = new long[profile.Zones.Count];
    }

    public HeartRateZoneProfile Profile { get; }

    public TimeSpan MaximumGap { get; }

    public HeartRateObservation? LastObservation => lastObservation;

    public HeartRateZone? CurrentZone => lastObservation is { } observation
        ? Profile.GetZone(observation.BeatsPerMinute)
        : null;

    public double? AverageBeatsPerMinute => measurementCount == 0
        ? null
        : totalBeatsPerMinute / (double)measurementCount;

    public ushort? MaximumBeatsPerMinute => maximumBeatsPerMinute;

    public void Add(HeartRateObservation observation)
    {
        if (isPaused)
        {
            return;
        }

        AddElapsedTime(observation.CapturedAt);
        lastObservation = observation;
        totalBeatsPerMinute += observation.BeatsPerMinute;
        measurementCount++;
        maximumBeatsPerMinute = maximumBeatsPerMinute is ushort maximum
            ? ushort.Max(maximum, observation.BeatsPerMinute)
            : observation.BeatsPerMinute;
    }

    public void Pause(DateTimeOffset timestamp)
    {
        if (isPaused)
        {
            return;
        }

        AddElapsedTime(timestamp);
        lastObservation = null;
        isPaused = true;
    }

    public void Resume() => isPaused = false;

    public IReadOnlyList<HeartRateZoneDuration> GetTimeInZones()
    {
        var durations = new HeartRateZoneDuration[zoneDurationTicks.Length];
        for (var index = 0; index < durations.Length; index++)
        {
            durations[index] = new(Profile.Zones[index], TimeSpan.FromTicks(zoneDurationTicks[index]));
        }

        return durations;
    }

    public void Reset()
    {
        Array.Clear(zoneDurationTicks);
        lastObservation = null;
        totalBeatsPerMinute = 0;
        measurementCount = 0;
        maximumBeatsPerMinute = null;
        isPaused = false;
    }

    private void AddElapsedTime(DateTimeOffset timestamp)
    {
        if (lastObservation is not { } previous)
        {
            return;
        }

        var elapsed = timestamp - previous.CapturedAt;
        if (elapsed > TimeSpan.Zero && elapsed <= MaximumGap)
        {
            zoneDurationTicks[Profile.GetZoneIndex(previous.BeatsPerMinute)] += elapsed.Ticks;
        }
    }
}