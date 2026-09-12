using System.Globalization;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard;

public sealed class TelemetryChart : GraphicsView
{
    public static readonly BindableProperty PointsProperty = BindableProperty.Create(
        nameof(Points),
        typeof(IReadOnlyList<TelemetrySample>),
        typeof(TelemetryChart),
        defaultValue: (IReadOnlyList<TelemetrySample>)[],
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.Points = value as IReadOnlyList<TelemetrySample> ?? [];
            chart.Invalidate();
        });

    public static readonly BindableProperty StrokeColorProperty = BindableProperty.Create(
        nameof(StrokeColor),
        typeof(Color),
        typeof(TelemetryChart),
        defaultValue: Colors.Teal,
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.StrokeColor = value as Color ?? Colors.Teal;
            chart.Invalidate();
        });

    public static readonly BindableProperty ValueLabelFormatProperty = BindableProperty.Create(
        nameof(ValueLabelFormat),
        typeof(string),
        typeof(TelemetryChart),
        defaultValue: "F0",
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.ValueLabelFormat = value as string ?? "F0";
            chart.Invalidate();
        });

    public static readonly BindableProperty ReferenceValuesProperty = BindableProperty.Create(
        nameof(ReferenceValues),
        typeof(IReadOnlyList<double>),
        typeof(TelemetryChart),
        defaultValue: (IReadOnlyList<double>)[],
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.ReferenceValues = value as IReadOnlyList<double> ?? [];
            chart.Invalidate();
        });

    public static readonly BindableProperty BackgroundRegionsProperty = BindableProperty.Create(
        nameof(BackgroundRegions),
        typeof(IReadOnlyList<TransitionRegion<Color>>),
        typeof(TelemetryChart),
        defaultValue: (IReadOnlyList<TransitionRegion<Color>>)[],
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.BackgroundRegions = value as IReadOnlyList<TransitionRegion<Color>> ?? [];
            chart.Invalidate();
        });

    public static readonly BindableProperty TimeRangeStartProperty = BindableProperty.Create(
        nameof(TimeRangeStart),
        typeof(DateTimeOffset?),
        typeof(TelemetryChart),
        defaultValue: null,
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.TimeRangeStart = value as DateTimeOffset?;
            chart.Invalidate();
        });

    public static readonly BindableProperty TimeRangeEndProperty = BindableProperty.Create(
        nameof(TimeRangeEnd),
        typeof(DateTimeOffset?),
        typeof(TelemetryChart),
        defaultValue: null,
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.TimeRangeEnd = value as DateTimeOffset?;
            chart.Invalidate();
        });

    public static readonly BindableProperty HighContrastProperty = BindableProperty.Create(
        nameof(HighContrast),
        typeof(bool),
        typeof(TelemetryChart),
        defaultValue: false,
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.HighContrast = value is true;
            chart.Invalidate();
        });

    public static readonly BindableProperty ShowTimeAxisProperty = BindableProperty.Create(
        nameof(ShowTimeAxis),
        typeof(bool),
        typeof(TelemetryChart),
        defaultValue: true,
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.ShowTimeAxis = value is true;
            chart.Invalidate();
        });

    private readonly TelemetryDrawable drawable = new();

    public TelemetryChart()
    {
        Drawable = drawable;
    }

    public IReadOnlyList<TelemetrySample> Points
    {
        get => (IReadOnlyList<TelemetrySample>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Color StrokeColor
    {
        get => (Color)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    public string ValueLabelFormat
    {
        get => (string)GetValue(ValueLabelFormatProperty);
        set => SetValue(ValueLabelFormatProperty, value);
    }

    public IReadOnlyList<double> ReferenceValues
    {
        get => (IReadOnlyList<double>)GetValue(ReferenceValuesProperty);
        set => SetValue(ReferenceValuesProperty, value);
    }

    public IReadOnlyList<TransitionRegion<Color>> BackgroundRegions
    {
        get => (IReadOnlyList<TransitionRegion<Color>>)GetValue(BackgroundRegionsProperty);
        set => SetValue(BackgroundRegionsProperty, value);
    }

    public DateTimeOffset? TimeRangeStart
    {
        get => (DateTimeOffset?)GetValue(TimeRangeStartProperty);
        set => SetValue(TimeRangeStartProperty, value);
    }

    public DateTimeOffset? TimeRangeEnd
    {
        get => (DateTimeOffset?)GetValue(TimeRangeEndProperty);
        set => SetValue(TimeRangeEndProperty, value);
    }

    public bool HighContrast
    {
        get => (bool)GetValue(HighContrastProperty);
        set => SetValue(HighContrastProperty, value);
    }

    public bool ShowTimeAxis
    {
        get => (bool)GetValue(ShowTimeAxisProperty);
        set => SetValue(ShowTimeAxisProperty, value);
    }

    private sealed class TelemetryDrawable : IDrawable
    {
        private const float Inset = 3;
        private const float VerticalLabelWidth = 32;
        private const float VerticalLabelHeight = 12;
        private const float TimeLabelHeight = 12;
        private static readonly float[] AverageDashPattern = [4f, 3f];
        private static readonly float[] ReferenceDashPattern = [2f, 2f];

        public IReadOnlyList<TelemetrySample> Points { get; set; } = [];

        public Color StrokeColor { get; set; } = Colors.Teal;

        public string ValueLabelFormat { get; set; } = "F0";

        public IReadOnlyList<double> ReferenceValues { get; set; } = [];

        public IReadOnlyList<TransitionRegion<Color>> BackgroundRegions { get; set; } = [];

        public DateTimeOffset? TimeRangeStart { get; set; }

        public DateTimeOffset? TimeRangeEnd { get; set; }

        public bool HighContrast { get; set; }

        public bool ShowTimeAxis { get; set; } = true;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var timeAxisHeight = ShowTimeAxis ? TimeLabelHeight : 0;
            var plot = new RectF(
                Inset + VerticalLabelWidth,
                Inset + (VerticalLabelHeight / 2),
                dirtyRect.Width - (Inset * 2) - VerticalLabelWidth,
                dirtyRect.Height - (Inset * 2) - VerticalLabelHeight - timeAxisHeight);
            if (plot.Width <= 0 || plot.Height <= 0)
            {
                return;
            }

            if (Points.Count == 0)
            {
                return;
            }

            var minimum = Points.Min(point => point.Value);
            var maximum = Points.Max(point => point.Value);
            var range = maximum - minimum;
            DrawBackgroundBands(canvas, plot);
            DrawRangeGuides(canvas, plot, minimum, maximum);
            if (ShowTimeAxis)
            {
                DrawTimeAxis(canvas, plot);
            }
            canvas.StrokeColor = Color.FromArgb(HighContrast ? "33433D" : "66746D");
            canvas.StrokeDashPattern = ReferenceDashPattern;
            foreach (var referenceValue in ReferenceValues)
            {
                if (!double.IsFinite(referenceValue) || referenceValue < minimum || referenceValue > maximum)
                {
                    continue;
                }

                var referenceY = ToY(referenceValue, minimum, range, plot);
                canvas.DrawLine(plot.Left, referenceY, plot.Right, referenceY);
            }

            canvas.StrokeDashPattern = [];
            var average = Points.Average(point => point.Value);
            var averageY = ToY(average, minimum, range, plot);
            canvas.StrokeColor = StrokeColor;
            canvas.StrokeSize = HighContrast ? 1.5f : 1f;
            canvas.StrokeDashPattern = AverageDashPattern;
            canvas.DrawLine(plot.Left, averageY, plot.Right, averageY);
            canvas.StrokeDashPattern = [];
            if (Points.Count < 2)
            {
                return;
            }

            canvas.StrokeColor = StrokeColor;
            canvas.StrokeSize = HighContrast ? 3f : 2f;
            canvas.SaveState();
            canvas.ClipRectangle(plot);
            for (var index = 1; index < Points.Count; index++)
            {
                var previous = Points[index - 1];
                var current = Points[index];
                var previousX = ToX(previous.CapturedAt, plot);
                var currentX = ToX(current.CapturedAt, plot);
                var previousY = ToY(previous.Value, minimum, range, plot);
                var currentY = ToY(current.Value, minimum, range, plot);
                canvas.DrawLine(previousX, previousY, currentX, currentY);
            }

            canvas.RestoreState();
        }

        private void DrawBackgroundBands(ICanvas canvas, RectF plot)
        {
            canvas.SaveState();
            canvas.ClipRectangle(plot);
            for (var index = 0; index < BackgroundRegions.Count; index++)
            {
                var region = BackgroundRegions[index];
                var left = Math.Max(plot.Left, ToX(region.Start, plot));
                var rightTimestamp = index == BackgroundRegions.Count - 1
                    ? TimeRangeEnd ?? region.End
                    : region.End;
                var right = Math.Min(plot.Right, ToX(rightTimestamp, plot));
                if (right <= left)
                {
                    continue;
                }

                canvas.FillColor = region.State;
                canvas.FillRectangle(left, plot.Top, right - left, plot.Height);
            }

            canvas.RestoreState();
        }

        private void DrawRangeGuides(ICanvas canvas, RectF plot, double minimum, double maximum)
        {
            var guideColor = Color.FromArgb(HighContrast ? "8A9A94" : "D6DED8");
            canvas.StrokeColor = guideColor;
            canvas.StrokeSize = HighContrast ? 1.5f : 1f;
            canvas.StrokeDashPattern = [];
            canvas.DrawLine(plot.Left, plot.Top, plot.Right, plot.Top);
            canvas.DrawLine(plot.Left, plot.Bottom, plot.Right, plot.Bottom);
            canvas.FontColor = guideColor;
            canvas.FontSize = 9;
            canvas.DrawString(
                maximum.ToString(ValueLabelFormat, CultureInfo.InvariantCulture),
                new RectF(Inset, plot.Top - (VerticalLabelHeight / 2), VerticalLabelWidth - Inset, VerticalLabelHeight),
                HorizontalAlignment.Right,
                VerticalAlignment.Center);
            canvas.DrawString(
                minimum.ToString(ValueLabelFormat, CultureInfo.InvariantCulture),
                new RectF(Inset, plot.Bottom - (VerticalLabelHeight / 2), VerticalLabelWidth - Inset, VerticalLabelHeight),
                HorizontalAlignment.Right,
                VerticalAlignment.Center);
        }

        private void DrawTimeAxis(ICanvas canvas, RectF plot)
        {
            var duration = GetTimeRange().Duration;
            var labelTop = plot.Bottom + Inset;
            var labelColor = Color.FromArgb(HighContrast ? "8A9A94" : "66746D");
            canvas.FontColor = labelColor;
            canvas.FontSize = 9;
            canvas.DrawString("0:00", new RectF(plot.Left, labelTop, plot.Width / 2, TimeLabelHeight), HorizontalAlignment.Left, VerticalAlignment.Center);
            if (duration > TimeSpan.Zero)
            {
                canvas.DrawString(FormatElapsedTime(duration), new RectF(plot.Left + (plot.Width / 2), labelTop, plot.Width / 2, TimeLabelHeight), HorizontalAlignment.Right, VerticalAlignment.Center);
            }
        }

        private static float ToY(double value, double minimum, double range, RectF plot) =>
            plot.Bottom - (float)(range > 0 ? (value - minimum) / range * plot.Height : plot.Height / 2);

        private float ToX(DateTimeOffset timestamp, RectF plot)
        {
            var (start, duration) = GetTimeRange();
            if (duration <= TimeSpan.Zero)
            {
                return plot.Left;
            }

            return plot.Left + (float)((timestamp - start).TotalMilliseconds / duration.TotalMilliseconds * plot.Width);
        }

        private (DateTimeOffset Start, TimeSpan Duration) GetTimeRange()
        {
            var start = TimeRangeStart ?? Points[0].CapturedAt;
            var end = TimeRangeEnd ?? Points[^1].CapturedAt;
            return (start, end - start);
        }

        private static string FormatElapsedTime(TimeSpan duration) => duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}