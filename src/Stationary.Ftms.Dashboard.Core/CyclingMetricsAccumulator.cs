namespace Stationary.Ftms.Dashboard.Core;

public sealed class CyclingMetricsAccumulator(TimeSpan maximumSampleGap)
{
    private static readonly TimeSpan NormalizedPowerWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumPeakWindow = TimeSpan.FromMinutes(5);
    private readonly Queue<TelemetrySample> samples = [];
    private TelemetrySample? previousSample;
    private double activeSeconds;
    private double totalWorkJoules;
    private double normalizedPowerFourthTotal;
    private int normalizedPowerSampleCount;
    private double? peakPower5Seconds;
    private double? peakPower30Seconds;
    private double? peakPower1Minute;
    private double? peakPower5Minutes;

    public TimeSpan MaximumSampleGap { get; } = maximumSampleGap > TimeSpan.Zero
        ? maximumSampleGap
        : throw new ArgumentOutOfRangeException(nameof(maximumSampleGap));

    public void Add(TelemetrySample sample)
    {
        if (previousSample is { } previous)
        {
            if (sample.CapturedAt <= previous.CapturedAt)
            {
                return;
            }

            var interval = sample.CapturedAt - previous.CapturedAt;
            if (interval <= MaximumSampleGap)
            {
                var seconds = interval.TotalSeconds;
                totalWorkJoules += ((previous.Value + sample.Value) / 2d) * seconds;
                activeSeconds += seconds;
            }
            else
            {
                samples.Clear();
            }
        }

        previousSample = sample;
        samples.Enqueue(sample);
        Trim(sample.CapturedAt - MaximumPeakWindow);
        UpdatePeak(TimeSpan.FromSeconds(5), ref peakPower5Seconds);
        UpdatePeak(TimeSpan.FromSeconds(30), ref peakPower30Seconds);
        UpdatePeak(TimeSpan.FromMinutes(1), ref peakPower1Minute);
        UpdatePeak(MaximumPeakWindow, ref peakPower5Minutes);
        UpdateNormalizedPower();
    }

    public CyclingMetrics GetMetrics(ushort? functionalThresholdPowerWatts = null)
    {
        double? normalizedPower = normalizedPowerSampleCount > 0
            ? double.Pow(normalizedPowerFourthTotal / normalizedPowerSampleCount, 0.25d)
            : null;
        double? intensityFactor = normalizedPower is double power && functionalThresholdPowerWatts is > 0
            ? power / functionalThresholdPowerWatts.Value
            : null;
        double? trainingStressScore = normalizedPower is double normalized
            && intensityFactor is double intensity
            && functionalThresholdPowerWatts is > 0
                ? activeSeconds * normalized * intensity / (functionalThresholdPowerWatts.Value * 36d)
                : null;

        return new(
            totalWorkJoules / 1000d,
            peakPower5Seconds,
            peakPower30Seconds,
            peakPower1Minute,
            peakPower5Minutes,
            normalizedPower,
            intensityFactor,
            trainingStressScore);
    }

    public void Reset()
    {
        samples.Clear();
        previousSample = null;
        activeSeconds = 0d;
        totalWorkJoules = 0d;
        normalizedPowerFourthTotal = 0d;
        normalizedPowerSampleCount = 0;
        peakPower5Seconds = null;
        peakPower30Seconds = null;
        peakPower1Minute = null;
        peakPower5Minutes = null;
    }

    private void UpdatePeak(TimeSpan window, ref double? peak)
    {
        if (samples.Count == 0 || samples.Last().CapturedAt - samples.First().CapturedAt < window)
        {
            return;
        }

        var cutoff = samples.Last().CapturedAt - window;
        var average = samples
            .Where(sample => sample.CapturedAt >= cutoff)
            .Average(sample => sample.Value);
        peak = peak is double current ? double.Max(current, average) : average;
    }

    private void UpdateNormalizedPower()
    {
        if (samples.Count == 0 || samples.Last().CapturedAt - samples.First().CapturedAt < NormalizedPowerWindow)
        {
            return;
        }

        var cutoff = samples.Last().CapturedAt - NormalizedPowerWindow;
        var rollingAverage = samples
            .Where(sample => sample.CapturedAt >= cutoff)
            .Average(sample => sample.Value);
        normalizedPowerFourthTotal += double.Pow(rollingAverage, 4d);
        normalizedPowerSampleCount++;
    }

    private void Trim(DateTimeOffset cutoff)
    {
        while (samples.TryPeek(out var oldest) && oldest.CapturedAt < cutoff)
        {
            samples.Dequeue();
        }
    }
}