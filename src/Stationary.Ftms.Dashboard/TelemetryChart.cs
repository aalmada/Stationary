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

    private sealed class TelemetryDrawable : IDrawable
    {
        private static readonly float[] AverageDashPattern = [4f, 3f];

        public IReadOnlyList<TelemetrySample> Points { get; set; } = [];

        public Color StrokeColor { get; set; } = Colors.Teal;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            const float inset = 3;
            var width = dirtyRect.Width - (inset * 2);
            var height = dirtyRect.Height - (inset * 2);
            if (width <= 0 || height <= 0)
            {
                return;
            }

            canvas.StrokeColor = Color.FromArgb("D6DED8");
            canvas.StrokeSize = 1;
            canvas.DrawLine(inset, inset + (height / 2), inset + width, inset + (height / 2));
            if (Points.Count == 0)
            {
                return;
            }

            var minimum = Points.Min(point => point.Value);
            var maximum = Points.Max(point => point.Value);
            var range = maximum - minimum;
            var average = Points.Average(point => point.Value);
            var averageY = inset + height - (float)(range > 0 ? (average - minimum) / range * height : height / 2);
            canvas.StrokeColor = StrokeColor;
            canvas.StrokeDashPattern = AverageDashPattern;
            canvas.DrawLine(inset, averageY, inset + width, averageY);
            canvas.StrokeDashPattern = [];
            if (Points.Count < 2)
            {
                return;
            }

            canvas.StrokeColor = StrokeColor;
            canvas.StrokeSize = 2;
            for (var index = 1; index < Points.Count; index++)
            {
                var previous = Points[index - 1];
                var current = Points[index];
                var previousX = inset + (width * (index - 1) / (Points.Count - 1));
                var currentX = inset + (width * index / (Points.Count - 1));
                var previousY = inset + height - (float)(range > 0 ? (previous.Value - minimum) / range * height : height / 2);
                var currentY = inset + height - (float)(range > 0 ? (current.Value - minimum) / range * height : height / 2);
                canvas.DrawLine(previousX, previousY, currentX, currentY);
            }
        }
    }
}