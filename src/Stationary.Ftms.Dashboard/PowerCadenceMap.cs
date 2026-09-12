using System.Globalization;

namespace Stationary.Ftms.Dashboard;

public sealed class PowerCadenceMap : GraphicsView
{
    public static readonly BindableProperty PointsProperty = BindableProperty.Create(
        nameof(Points),
        typeof(IReadOnlyList<PowerCadenceSample>),
        typeof(PowerCadenceMap),
        defaultValue: (IReadOnlyList<PowerCadenceSample>)[],
        propertyChanged: static (bindable, _, value) =>
        {
            var map = (PowerCadenceMap)bindable;
            map.drawable.Points = value as IReadOnlyList<PowerCadenceSample> ?? [];
            map.Invalidate();
        });

    public static readonly BindableProperty AccentColorProperty = BindableProperty.Create(
        nameof(AccentColor),
        typeof(Color),
        typeof(PowerCadenceMap),
        defaultValue: Colors.Teal,
        propertyChanged: static (bindable, _, value) =>
        {
            var map = (PowerCadenceMap)bindable;
            map.drawable.AccentColor = value as Color ?? Colors.Teal;
            map.Invalidate();
        });

    public static readonly BindableProperty HighContrastProperty = BindableProperty.Create(
        nameof(HighContrast),
        typeof(bool),
        typeof(PowerCadenceMap),
        defaultValue: false,
        propertyChanged: static (bindable, _, value) =>
        {
            var map = (PowerCadenceMap)bindable;
            map.drawable.HighContrast = value is true;
            map.Invalidate();
        });

    private readonly PowerCadenceDrawable drawable = new();

    public PowerCadenceMap()
    {
        Drawable = drawable;
    }

    public IReadOnlyList<PowerCadenceSample> Points
    {
        get => (IReadOnlyList<PowerCadenceSample>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public bool HighContrast
    {
        get => (bool)GetValue(HighContrastProperty);
        set => SetValue(HighContrastProperty, value);
    }

    private sealed class PowerCadenceDrawable : IDrawable
    {
        private const float Inset = 5;
        private const float VerticalLabelWidth = 28;
        private const float HorizontalLabelHeight = 13;

        public IReadOnlyList<PowerCadenceSample> Points { get; set; } = [];

        public Color AccentColor { get; set; } = Colors.Teal;

        public bool HighContrast { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var plot = new RectF(
                Inset + VerticalLabelWidth,
                Inset,
                dirtyRect.Width - (Inset * 2) - VerticalLabelWidth,
                dirtyRect.Height - (Inset * 2) - HorizontalLabelHeight);
            if (plot.Width <= 0 || plot.Height <= 0)
            {
                return;
            }

            if (Points.Count == 0)
            {
                return;
            }

            var minimumCadence = Points.Min(point => point.CadenceRpm);
            var maximumCadence = Points.Max(point => point.CadenceRpm);
            var cadenceSpan = maximumCadence - minimumCadence;
            var cadencePadding = cadenceSpan * 0.1d;
            if (cadencePadding < 5d)
            {
                cadencePadding = 5d;
            }

            minimumCadence -= cadencePadding;
            if (minimumCadence < 0d)
            {
                minimumCadence = 0d;
            }

            maximumCadence += cadencePadding;
            var maximumPower = Points.Max(point => point.PowerWatts) * 1.1d;
            if (maximumPower < 100d)
            {
                maximumPower = 100d;
            }

            DrawGrid(canvas, plot, minimumCadence, maximumCadence, maximumPower, HighContrast);
            canvas.FillColor = AccentColor.WithAlpha(HighContrast ? 0.65f : 0.45f);
            for (var index = 0; index < Points.Count - 1; index++)
            {
                var point = Points[index];
                canvas.FillCircle(
                    ToX(point.CadenceRpm, minimumCadence, maximumCadence, plot),
                    ToY(point.PowerWatts, maximumPower, plot),
                    HighContrast ? 2.75f : 2f);
            }

            var latest = Points[^1];
            var latestX = ToX(latest.CadenceRpm, minimumCadence, maximumCadence, plot);
            var latestY = ToY(latest.PowerWatts, maximumPower, plot);
            canvas.FillColor = Colors.White;
            canvas.FillCircle(latestX, latestY, HighContrast ? 8f : 7f);
            canvas.StrokeColor = Color.FromArgb(HighContrast ? "17201D" : "33433D");
            canvas.StrokeSize = HighContrast ? 2.5f : 2f;
            canvas.DrawCircle(latestX, latestY, HighContrast ? 8f : 7f);
            canvas.FillColor = AccentColor;
            canvas.FillCircle(latestX, latestY, HighContrast ? 4.5f : 4f);
        }

        private static void DrawGrid(ICanvas canvas, RectF plot, double minimumCadence, double maximumCadence, double maximumPower, bool highContrast)
        {
            canvas.StrokeColor = Color.FromArgb(highContrast ? "8A9A94" : "D6DED8");
            canvas.StrokeSize = highContrast ? 1.5f : 1f;
            canvas.FontColor = Color.FromArgb(highContrast ? "33433D" : "66746D");
            canvas.FontSize = 9;
            for (var index = 0; index < 3; index++)
            {
                var fraction = index / 2f;
                var horizontalOffset = plot.Height * fraction;
                var verticalOffset = plot.Width * fraction;
                var power = maximumPower * (1d - fraction);
                var cadence = minimumCadence + ((maximumCadence - minimumCadence) * fraction);
                canvas.DrawLine(plot.Left, plot.Top + horizontalOffset, plot.Right, plot.Top + horizontalOffset);
                canvas.DrawLine(plot.Left + verticalOffset, plot.Top, plot.Left + verticalOffset, plot.Bottom);
                canvas.DrawString(
                    power.ToString("F0", CultureInfo.InvariantCulture),
                    new RectF(Inset, plot.Top + horizontalOffset - 6, VerticalLabelWidth - 3, 12),
                    HorizontalAlignment.Right,
                    VerticalAlignment.Center);
                canvas.DrawString(
                    cadence.ToString("F0", CultureInfo.InvariantCulture),
                    new RectF(plot.Left + verticalOffset - 15, plot.Bottom + 1, 30, HorizontalLabelHeight),
                    HorizontalAlignment.Center,
                    VerticalAlignment.Center);
            }
        }

        private static float ToX(double cadence, double minimumCadence, double maximumCadence, RectF plot) =>
            plot.Left + (float)((cadence - minimumCadence) / (maximumCadence - minimumCadence) * plot.Width);

        private static float ToY(double power, double maximumPower, RectF plot) =>
            plot.Bottom - (float)(power / maximumPower * plot.Height);
    }
}