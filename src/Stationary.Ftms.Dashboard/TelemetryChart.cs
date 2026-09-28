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

    public static readonly BindableProperty ComparisonPointsProperty = BindableProperty.Create(
        nameof(ComparisonPoints),
        typeof(IReadOnlyList<TelemetrySample>),
        typeof(TelemetryChart),
        defaultValue: (IReadOnlyList<TelemetrySample>)[],
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.ComparisonPoints = value as IReadOnlyList<TelemetrySample> ?? [];
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

    public static readonly BindableProperty HoverTimestampProperty = BindableProperty.Create(
        nameof(HoverTimestamp),
        typeof(DateTimeOffset?),
        typeof(TelemetryChart),
        defaultValue: null,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.HoverTimestamp = value as DateTimeOffset?;
            chart.Invalidate();
        });

    public static readonly BindableProperty EventMarkersProperty = BindableProperty.Create(
        nameof(EventMarkers),
        typeof(IReadOnlyList<SessionEventMarker>),
        typeof(TelemetryChart),
        defaultValue: (IReadOnlyList<SessionEventMarker>)[],
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.EventMarkers = value as IReadOnlyList<SessionEventMarker> ?? [];
            chart.Invalidate();
        });

    public static readonly BindableProperty MinimumValueProperty = BindableProperty.Create(
        nameof(MinimumValue),
        typeof(double?),
        typeof(TelemetryChart),
        defaultValue: null,
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.MinimumValue = value as double?;
            chart.Invalidate();
        });

    public static readonly BindableProperty MaximumValueProperty = BindableProperty.Create(
        nameof(MaximumValue),
        typeof(double?),
        typeof(TelemetryChart),
        defaultValue: null,
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.MaximumValue = value as double?;
            chart.Invalidate();
        });

    public static readonly BindableProperty MaximumGapProperty = BindableProperty.Create(
        nameof(MaximumGap),
        typeof(TimeSpan),
        typeof(TelemetryChart),
        defaultValue: TimeSpan.FromSeconds(5),
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.MaximumGap = value is TimeSpan gap ? gap : TimeSpan.FromSeconds(5);
            chart.Invalidate();
        });

    public static readonly BindableProperty SelectionStartProperty = BindableProperty.Create(
        nameof(SelectionStart),
        typeof(DateTimeOffset?),
        typeof(TelemetryChart),
        defaultValue: null,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.SelectionStart = value as DateTimeOffset?;
            chart.Invalidate();
        });

    public static readonly BindableProperty SelectionEndProperty = BindableProperty.Create(
        nameof(SelectionEnd),
        typeof(DateTimeOffset?),
        typeof(TelemetryChart),
        defaultValue: null,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: static (bindable, _, value) =>
        {
            var chart = (TelemetryChart)bindable;
            chart.drawable.SelectionEnd = value as DateTimeOffset?;
            chart.Invalidate();
        });

    private readonly TelemetryDrawable drawable = new();

    public TelemetryChart()
    {
        Drawable = drawable;
        var pointer = new PointerGestureRecognizer();
        pointer.PointerMoved += OnPointerMoved;
        pointer.PointerExited += OnPointerExited;
        pointer.PointerPressed += OnPointerPressed;
        pointer.PointerReleased += OnPointerReleased;
        GestureRecognizers.Add(pointer);
    }

    public IReadOnlyList<TelemetrySample> Points
    {
        get => (IReadOnlyList<TelemetrySample>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IReadOnlyList<TelemetrySample> ComparisonPoints
    {
        get => (IReadOnlyList<TelemetrySample>)GetValue(ComparisonPointsProperty);
        set => SetValue(ComparisonPointsProperty, value);
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

    public DateTimeOffset? HoverTimestamp
    {
        get => (DateTimeOffset?)GetValue(HoverTimestampProperty);
        set => SetValue(HoverTimestampProperty, value);
    }

    public IReadOnlyList<SessionEventMarker> EventMarkers
    {
        get => (IReadOnlyList<SessionEventMarker>)GetValue(EventMarkersProperty);
        set => SetValue(EventMarkersProperty, value);
    }

    public double? MinimumValue
    {
        get => (double?)GetValue(MinimumValueProperty);
        set => SetValue(MinimumValueProperty, value);
    }

    public double? MaximumValue
    {
        get => (double?)GetValue(MaximumValueProperty);
        set => SetValue(MaximumValueProperty, value);
    }

    public TimeSpan MaximumGap
    {
        get => (TimeSpan)GetValue(MaximumGapProperty);
        set => SetValue(MaximumGapProperty, value);
    }

    public DateTimeOffset? SelectionStart
    {
        get => (DateTimeOffset?)GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public DateTimeOffset? SelectionEnd
    {
        get => (DateTimeOffset?)GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (eventArgs.GetPosition(this) is not { } position || Width <= 0d)
        {
            return;
        }

        HoverTimestamp = drawable.ToTimestamp((float)position.X, (float)Width);
        if (isSelecting)
        {
            SelectionEnd = HoverTimestamp;
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs eventArgs) => HoverTimestamp = null;

    private bool isSelecting;

    private void OnPointerPressed(object? sender, PointerEventArgs eventArgs)
    {
        if (eventArgs.GetPosition(this) is not { } position)
        {
            return;
        }

        isSelecting = true;
        SelectionStart = drawable.ToTimestamp((float)position.X, (float)Width);
        SelectionEnd = SelectionStart;
    }

    private void OnPointerReleased(object? sender, PointerEventArgs eventArgs)
    {
        if (eventArgs.GetPosition(this) is { } position)
        {
            SelectionEnd = drawable.ToTimestamp((float)position.X, (float)Width);
        }

        isSelecting = false;
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

        public IReadOnlyList<TelemetrySample> ComparisonPoints { get; set; } = [];

        public Color StrokeColor { get; set; } = Colors.Teal;

        public string ValueLabelFormat { get; set; } = "F0";

        public IReadOnlyList<double> ReferenceValues { get; set; } = [];

        public IReadOnlyList<TransitionRegion<Color>> BackgroundRegions { get; set; } = [];

        public DateTimeOffset? TimeRangeStart { get; set; }

        public DateTimeOffset? TimeRangeEnd { get; set; }

        public bool HighContrast { get; set; }

        public bool ShowTimeAxis { get; set; } = true;

        public DateTimeOffset? HoverTimestamp { get; set; }

        public IReadOnlyList<SessionEventMarker> EventMarkers { get; set; } = [];

        public double? MinimumValue { get; set; }

        public double? MaximumValue { get; set; }

        public TimeSpan MaximumGap { get; set; } = TimeSpan.FromSeconds(5);

        public DateTimeOffset? SelectionStart { get; set; }

        public DateTimeOffset? SelectionEnd { get; set; }

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

            var minimum = MinimumValue ?? Points.Min(point => point.Value);
            var maximum = MaximumValue ?? Points.Max(point => point.Value);
            if (maximum <= minimum)
            {
                maximum = minimum + 1d;
            }

            var range = maximum - minimum;
            DrawBackgroundBands(canvas, plot);
            DrawSelection(canvas, plot);
            DrawEventMarkers(canvas, plot);
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
                canvas.FontColor = Color.FromArgb(HighContrast ? "33433D" : "66746D");
                canvas.FontSize = 8;
                canvas.DrawString(
                    referenceValue.ToString(ValueLabelFormat, CultureInfo.InvariantCulture),
                    new RectF(plot.Right - 32f, referenceY - 10f, 30f, 10f),
                    HorizontalAlignment.Right,
                    VerticalAlignment.Center);
            }

            canvas.StrokeDashPattern = [];
            var average = Points.Average(point => point.Value);
            var averageY = ToY(average, minimum, range, plot);
            canvas.StrokeColor = StrokeColor;
            canvas.StrokeSize = HighContrast ? 1.5f : 1f;
            canvas.StrokeDashPattern = AverageDashPattern;
            canvas.DrawLine(plot.Left, averageY, plot.Right, averageY);
            canvas.FontColor = StrokeColor;
            canvas.FontSize = 8;
            canvas.DrawString(
                $"avg {average.ToString(ValueLabelFormat, CultureInfo.InvariantCulture)}",
                new RectF(plot.Left + 3f, averageY - 11f, 62f, 10f),
                HorizontalAlignment.Left,
                VerticalAlignment.Center);
            canvas.StrokeDashPattern = [];
            if (Points.Count < 2)
            {
                return;
            }

            DrawSeries(canvas, ComparisonPoints, plot, minimum, range, StrokeColor.WithAlpha(0.38f), HighContrast ? 2f : 1.5f, AverageDashPattern);
            DrawSeries(canvas, Points, plot, minimum, range, StrokeColor, HighContrast ? 3f : 2f, []);
            DrawHover(canvas, plot, minimum, range);
        }

        private void DrawSeries(
            ICanvas canvas,
            IReadOnlyList<TelemetrySample> points,
            RectF plot,
            double minimum,
            double range,
            Color color,
            float strokeSize,
            float[] dashPattern)
        {
            if (points.Count < 2)
            {
                return;
            }

            canvas.StrokeColor = StrokeColor;
            canvas.StrokeColor = color;
            canvas.StrokeSize = strokeSize;
            canvas.StrokeDashPattern = dashPattern;
            canvas.SaveState();
            canvas.ClipRectangle(plot);
            for (var index = 1; index < points.Count; index++)
            {
                var previous = points[index - 1];
                var current = points[index];
                if (current.CapturedAt - previous.CapturedAt > MaximumGap)
                {
                    continue;
                }

                var previousX = ToX(previous.CapturedAt, plot);
                var currentX = ToX(current.CapturedAt, plot);
                var previousY = ToY(previous.Value, minimum, range, plot);
                var currentY = ToY(current.Value, minimum, range, plot);
                canvas.DrawLine(previousX, previousY, currentX, currentY);
            }

            canvas.RestoreState();
            canvas.StrokeDashPattern = [];
        }

        public DateTimeOffset? ToTimestamp(float pointerX, float viewWidth)
        {
            var plotLeft = Inset + VerticalLabelWidth;
            var plotWidth = viewWidth - (Inset * 2) - VerticalLabelWidth;
            var (start, duration) = GetTimeRange();
            if (plotWidth <= 0f || duration <= TimeSpan.Zero)
            {
                return null;
            }

            var fraction = double.Clamp((pointerX - plotLeft) / plotWidth, 0d, 1d);
            return start + TimeSpan.FromTicks((long)(duration.Ticks * fraction));
        }

        private void DrawEventMarkers(ICanvas canvas, RectF plot)
        {
            canvas.SaveState();
            canvas.ClipRectangle(plot);
            canvas.StrokeSize = HighContrast ? 2f : 1.5f;
            foreach (var marker in EventMarkers)
            {
                var x = ToX(marker.Timestamp, plot);
                if (x < plot.Left || x > plot.Right)
                {
                    continue;
                }

                canvas.StrokeColor = marker.Type switch
                {
                    SessionEventType.Lap => Color.FromArgb("147D70"),
                    SessionEventType.Pause or SessionEventType.Stop => Color.FromArgb("B54708"),
                    SessionEventType.Disconnect => Color.FromArgb("B42318"),
                    _ => Color.FromArgb("3679A8"),
                };
                canvas.DrawLine(x, plot.Top, x, plot.Bottom);
            }

            canvas.RestoreState();
        }

        private void DrawSelection(ICanvas canvas, RectF plot)
        {
            if (SelectionStart is not { } start || SelectionEnd is not { } end || start == end)
            {
                return;
            }

            var left = float.Max(plot.Left, float.Min(ToX(start, plot), ToX(end, plot)));
            var right = float.Min(plot.Right, float.Max(ToX(start, plot), ToX(end, plot)));
            if (right <= left)
            {
                return;
            }

            canvas.FillColor = Color.FromArgb(HighContrast ? "4433433D" : "2E147D70");
            canvas.FillRectangle(left, plot.Top, right - left, plot.Height);
        }

        private void DrawHover(ICanvas canvas, RectF plot, double minimum, double range)
        {
            if (HoverTimestamp is not { } timestamp)
            {
                return;
            }

            var x = ToX(timestamp, plot);
            if (x < plot.Left || x > plot.Right)
            {
                return;
            }

            var nearest = Points[0];
            var nearestDistance = (nearest.CapturedAt - timestamp).Duration();
            for (var index = 1; index < Points.Count; index++)
            {
                var distance = (Points[index].CapturedAt - timestamp).Duration();
                if (distance >= nearestDistance)
                {
                    continue;
                }

                nearest = Points[index];
                nearestDistance = distance;
            }

            canvas.StrokeColor = Color.FromArgb(HighContrast ? "17201D" : "52615B");
            canvas.StrokeSize = 1f;
            canvas.DrawLine(x, plot.Top, x, plot.Bottom);
            var y = ToY(nearest.Value, minimum, range, plot);
            canvas.FillColor = Colors.White;
            canvas.FillCircle(x, y, 4f);
            canvas.StrokeColor = StrokeColor;
            canvas.StrokeSize = 2f;
            canvas.DrawCircle(x, y, 4f);
            canvas.FontColor = Color.FromArgb("17201D");
            canvas.FontSize = 9;
            var label = $"{nearest.Value.ToString(ValueLabelFormat, CultureInfo.InvariantCulture)}  {timestamp.ToLocalTime():HH:mm:ss}";
            var labelLeft = float.Min(x + 5f, plot.Right - 92f);
            canvas.DrawString(label, new RectF(labelLeft, plot.Top + 2f, 92f, 14f), HorizontalAlignment.Left, VerticalAlignment.Center);
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