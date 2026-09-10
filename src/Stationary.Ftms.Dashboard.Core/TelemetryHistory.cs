namespace Stationary.Ftms.Dashboard.Core;

public readonly record struct TelemetrySample(DateTimeOffset CapturedAt, double Value);

public sealed class TelemetryHistory(TimeSpan window)
{
    private readonly Queue<TelemetrySample> samples = [];

    public TimeSpan Window { get; } = window > TimeSpan.Zero
        ? window
        : throw new ArgumentOutOfRangeException(nameof(window));

    public IReadOnlyCollection<TelemetrySample> Samples => samples;

    public void Add(TelemetrySample sample)
    {
        samples.Enqueue(sample);
        Trim(sample.CapturedAt);
    }

    public void Trim(DateTimeOffset capturedAt)
    {
        var cutoff = capturedAt - Window;
        while (samples.TryPeek(out var oldest) && oldest.CapturedAt < cutoff)
        {
            samples.Dequeue();
        }
    }
}