namespace Stationary.Ftms.Dashboard.Core;

public sealed class CyclingMetricsAccumulator(TimeSpan maximumSampleGap)
{
    private static readonly TimeSpan NormalizedPowerWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumPeakWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Peak5SecondsWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Peak30SecondsWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Peak1MinuteWindow = TimeSpan.FromMinutes(1);
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
        UpdateWindowMetrics();
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

    private void UpdateWindowMetrics()
    {
        if (!samples.TryPeek(out var oldest) || previousSample is not { } latest)
        {
            return;
        }

        var elapsed = latest.CapturedAt - oldest.CapturedAt;
        var cutoff5Seconds = latest.CapturedAt - Peak5SecondsWindow;
        var cutoff30Seconds = latest.CapturedAt - Peak30SecondsWindow;
        var cutoff1Minute = latest.CapturedAt - Peak1MinuteWindow;
        var cutoff5Minutes = latest.CapturedAt - MaximumPeakWindow;
        var sum5Seconds = 0d;
        var sum30Seconds = 0d;
        var sum1Minute = 0d;
        var sum5Minutes = 0d;
        var count5Seconds = 0;
        var count30Seconds = 0;
        var count1Minute = 0;
        var count5Minutes = 0;

        foreach (var sample in samples)
        {
            if (sample.CapturedAt >= cutoff5Seconds)
            {
                sum5Seconds += sample.Value;
                count5Seconds++;
            }

            if (sample.CapturedAt >= cutoff30Seconds)
            {
                sum30Seconds += sample.Value;
                count30Seconds++;
            }

            if (sample.CapturedAt >= cutoff1Minute)
            {
                sum1Minute += sample.Value;
                count1Minute++;
            }

            if (sample.CapturedAt >= cutoff5Minutes)
            {
                sum5Minutes += sample.Value;
                count5Minutes++;
            }
        }

        UpdatePeak(elapsed, Peak5SecondsWindow, sum5Seconds, count5Seconds, ref peakPower5Seconds);
        UpdatePeak(elapsed, Peak30SecondsWindow, sum30Seconds, count30Seconds, ref peakPower30Seconds);
        UpdatePeak(elapsed, Peak1MinuteWindow, sum1Minute, count1Minute, ref peakPower1Minute);
        UpdatePeak(elapsed, MaximumPeakWindow, sum5Minutes, count5Minutes, ref peakPower5Minutes);

        if (elapsed >= NormalizedPowerWindow)
        {
            normalizedPowerFourthTotal += double.Pow(sum30Seconds / count30Seconds, 4d);
            normalizedPowerSampleCount++;
        }
    }

    private static void UpdatePeak(TimeSpan elapsed, TimeSpan window, double sum, int count, ref double? peak)
    {
        if (elapsed < window)
        {
            return;
        }

        var average = sum / count;
        peak = peak is double current ? double.Max(current, average) : average;
    }

    private void Trim(DateTimeOffset cutoff)
    {
        while (samples.TryPeek(out var oldest) && oldest.CapturedAt < cutoff)
        {
            samples.Dequeue();
        }
    }
}