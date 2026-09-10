namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct TelemetryFieldSample(
    DateTimeOffset CapturedAt,
    bool IsReported,
    double? Value,
    bool ShouldAdvance);

public readonly record struct TelemetryFieldObservation(
    bool IsObserved,
    bool IsUnavailable,
    bool IsStalled,
    double? LastValue,
    DateTimeOffset? LastChangedAt)
{
    public static TelemetryFieldObservation Empty => default;

    public TelemetryFieldObservation Observe(TelemetryFieldSample sample, TimeSpan stallThreshold)
    {
        if (!sample.IsReported)
        {
            var omittedStalled = sample.ShouldAdvance && IsObserved && LastChangedAt is DateTimeOffset omittedChangedAt && sample.CapturedAt - omittedChangedAt >= stallThreshold;
            return this with { IsStalled = omittedStalled };
        }

        if (sample.IsReported && sample.Value is null)
        {
            return this with { IsUnavailable = true, IsStalled = false };
        }

        var changed = sample.Value is double value && (!IsObserved || LastValue != value);
        var observed = IsObserved || sample.Value.HasValue;
        var lastValue = changed ? sample.Value : LastValue;
        var lastChangedAt = changed ? sample.CapturedAt : LastChangedAt;
        var stalled = sample.ShouldAdvance && observed && lastChangedAt is DateTimeOffset changedAt && sample.CapturedAt - changedAt >= stallThreshold;

        return new(observed, false, stalled, lastValue, lastChangedAt);
    }
}