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

    public bool HighContrast
    {
        get => (bool)GetValue(HighContrastProperty);
        set => SetValue(HighContrastProperty, value);
    }

    private sealed class TelemetryDrawable : IDrawable
    {
        private const float Inset = 3;
        private const float VerticalLabelWidth = 32;
        private const float VerticalLabelHeight = 12;
        private static readonly float[] AverageDashPattern = [4f, 3f];
        private static readonly float[] ReferenceDashPattern = [2f, 2f];

        public IReadOnlyList<TelemetrySample> Points { get; set; } = [];

        public Color StrokeColor { get; set; } = Colors.Teal;

        public string ValueLabelFormat { get; set; } = "F0";

        public IReadOnlyList<double> ReferenceValues { get; set; } = [];

        public bool HighContrast { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var plot = new RectF(
                Inset + VerticalLabelWidth,
                Inset + (VerticalLabelHeight / 2),
                dirtyRect.Width - (Inset * 2) - VerticalLabelWidth,
                dirtyRect.Height - (Inset * 2) - VerticalLabelHeight);
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
            DrawRangeGuides(canvas, plot, minimum, maximum);
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
            for (var index = 1; index < Points.Count; index++)
            {
                var previous = Points[index - 1];
                var current = Points[index];
                var previousX = plot.Left + (plot.Width * (index - 1) / (Points.Count - 1));
                var currentX = plot.Left + (plot.Width * index / (Points.Count - 1));
                var previousY = ToY(previous.Value, minimum, range, plot);
                var currentY = ToY(current.Value, minimum, range, plot);
                canvas.DrawLine(previousX, previousY, currentX, currentY);
            }
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

        private static float ToY(double value, double minimum, double range, RectF plot) =>
            plot.Bottom - (float)(range > 0 ? (value - minimum) / range * plot.Height : plot.Height / 2);
    }
}