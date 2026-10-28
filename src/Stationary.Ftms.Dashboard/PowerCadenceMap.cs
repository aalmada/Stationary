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

    public static readonly BindableProperty TargetPowerWattsProperty = BindableProperty.Create(
        nameof(TargetPowerWatts),
        typeof(double?),
        typeof(PowerCadenceMap),
        defaultValue: null,
        propertyChanged: static (bindable, _, value) =>
        {
            var map = (PowerCadenceMap)bindable;
            map.drawable.TargetPowerWatts = value is double target ? target : null;
            map.Invalidate();
        });

    public static readonly BindableProperty MinimumCadenceRpmProperty = BindableProperty.Create(
        nameof(MinimumCadenceRpm),
        typeof(int?),
        typeof(PowerCadenceMap),
        defaultValue: null,
        propertyChanged: static (bindable, _, value) =>
        {
            var map = (PowerCadenceMap)bindable;
            map.drawable.MinimumCadenceRpm = value is int minimum ? minimum : null;
            map.Invalidate();
        });

    public static readonly BindableProperty MaximumCadenceRpmProperty = BindableProperty.Create(
        nameof(MaximumCadenceRpm),
        typeof(int?),
        typeof(PowerCadenceMap),
        defaultValue: null,
        propertyChanged: static (bindable, _, value) =>
        {
            var map = (PowerCadenceMap)bindable;
            map.drawable.MaximumCadenceRpm = value is int maximum ? maximum : null;
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

    public double? TargetPowerWatts
    {
        get => (double?)GetValue(TargetPowerWattsProperty);
        set => SetValue(TargetPowerWattsProperty, value);
    }

    public int? MinimumCadenceRpm
    {
        get => (int?)GetValue(MinimumCadenceRpmProperty);
        set => SetValue(MinimumCadenceRpmProperty, value);
    }

    public int? MaximumCadenceRpm
    {
        get => (int?)GetValue(MaximumCadenceRpmProperty);
        set => SetValue(MaximumCadenceRpmProperty, value);
    }

    private sealed class PowerCadenceDrawable : IDrawable
    {
        private const float Inset = 5;
        private const float VerticalLabelWidth = 28;
        private const float HorizontalLabelHeight = 13;

        public IReadOnlyList<PowerCadenceSample> Points { get; set; } = [];

        public Color AccentColor { get; set; } = Colors.Teal;

        public bool HighContrast { get; set; }

        public double? TargetPowerWatts { get; set; }

        public int? MinimumCadenceRpm { get; set; }

        public int? MaximumCadenceRpm { get; set; }

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

            if (Points.Count == 0
                && (TargetPowerWatts is not double targetPower || targetPower <= 0))
            {
                return;
            }

            var minimumCadence = Points.Count > 0
                ? Points.Min(point => point.CadenceRpm)
                : MinimumCadenceRpm ?? (MaximumCadenceRpm is int maximum ? maximum - 10 : 80);
            var maximumCadence = Points.Count > 0
                ? Points.Max(point => point.CadenceRpm)
                : MaximumCadenceRpm ?? (MinimumCadenceRpm is int minimum ? minimum + 10 : 100);
            if (MinimumCadenceRpm is int targetMinimumCadence)
            {
                minimumCadence = double.Min(minimumCadence, targetMinimumCadence);
            }

            if (MaximumCadenceRpm is int targetMaximumCadence)
            {
                maximumCadence = double.Max(maximumCadence, targetMaximumCadence);
            }

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
            var maximumObservedPower = Points.Count > 0 ? Points.Max(point => point.PowerWatts) : 0d;
            var targetPowerCeiling = TargetPowerWatts is double targetWatts
                ? targetWatts + double.Max(5d, targetWatts * 0.05d)
                : 0d;
            var maximumPower = double.Max(maximumObservedPower, targetPowerCeiling) * 1.1d;
            if (maximumPower < 100d)
            {
                maximumPower = 100d;
            }

            DrawGrid(canvas, plot, minimumCadence, maximumCadence, maximumPower, HighContrast);
            if (TargetPowerWatts is double powerTarget)
            {
                var targetTolerance = double.Max(5d, powerTarget * 0.05d);
                var zoneMinimumCadence = MinimumCadenceRpm ?? minimumCadence;
                var zoneMaximumCadence = MaximumCadenceRpm ?? maximumCadence;
                var zoneLeft = ToX(zoneMinimumCadence, minimumCadence, maximumCadence, plot);
                var zoneRight = ToX(zoneMaximumCadence, minimumCadence, maximumCadence, plot);
                var zoneTop = ToY(powerTarget + targetTolerance, maximumPower, plot);
                var zoneBottom = ToY(double.Max(0d, powerTarget - targetTolerance), maximumPower, plot);
                var targetZone = new RectF(zoneLeft, zoneTop, zoneRight - zoneLeft, zoneBottom - zoneTop);
                canvas.FillColor = Color.FromArgb(HighContrast ? "5571D6A8" : "3371D6A8");
                canvas.FillRectangle(targetZone);
                canvas.StrokeColor = Color.FromArgb(HighContrast ? "147D54" : "428B69");
                canvas.StrokeSize = HighContrast ? 1.5f : 1f;
                canvas.DrawRectangle(targetZone);
            }

            for (var index = 0; index < Points.Count - 1; index++)
            {
                var point = Points[index];
                var freshness = (index + 1f) / Points.Count;
                canvas.FillColor = AccentColor.WithAlpha((HighContrast ? 0.25f : 0.15f) + (freshness * 0.35f));
                canvas.FillCircle(
                    ToX(point.CadenceRpm, minimumCadence, maximumCadence, plot),
                    ToY(point.PowerWatts, maximumPower, plot),
                    HighContrast ? 2.75f : 2f);
            }

            if (Points.Count == 0)
            {
                return;
            }

            var latest = Points[^1];
            var latestX = ToX(latest.CadenceRpm, minimumCadence, maximumCadence, plot);
            var latestY = ToY(latest.PowerWatts, maximumPower, plot);
            var isInTarget = (TargetPowerWatts is not double target
                    || double.Abs(latest.PowerWatts - target) <= double.Max(5d, target * 0.05d))
                && (MinimumCadenceRpm is not int cadenceMinimum || latest.CadenceRpm >= cadenceMinimum)
                && (MaximumCadenceRpm is not int cadenceMaximum || latest.CadenceRpm <= cadenceMaximum);
            canvas.FillColor = Colors.White;
            canvas.FillCircle(latestX, latestY, HighContrast ? 8f : 7f);
            canvas.StrokeColor = Color.FromArgb(HighContrast ? "17201D" : "33433D");
            canvas.StrokeSize = HighContrast ? 2.5f : 2f;
            canvas.DrawCircle(latestX, latestY, HighContrast ? 8f : 7f);
            canvas.FillColor = TargetPowerWatts is null
                ? AccentColor
                : isInTarget
                    ? Color.FromArgb("23845E")
                    : Color.FromArgb("C84B4B");
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