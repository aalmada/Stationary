using System.Diagnostics;
using System.Globalization;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard;

public sealed class PowerCadenceMap : GraphicsView
{
    public static readonly BindableProperty SpeedKilometersPerHourProperty = BindableProperty.Create(
        nameof(SpeedKilometersPerHour),
        typeof(double),
        typeof(PowerCadenceMap),
        defaultValue: 0d,
        propertyChanged: static (bindable, _, value) =>
        {
            var map = (PowerCadenceMap)bindable;
            map.drawable.SpeedKilometersPerHour = value is double speed && double.IsFinite(speed) && speed > 0d ? speed : 0d;
            map.UpdateAnimationState();
            map.Invalidate();
        });

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

    public static readonly BindableProperty AxisMinimumCadenceRpmProperty = BindableProperty.Create(
        nameof(AxisMinimumCadenceRpm),
        typeof(int?),
        typeof(PowerCadenceMap),
        defaultValue: null,
        propertyChanged: static (bindable, _, value) =>
        {
            var map = (PowerCadenceMap)bindable;
            map.drawable.AxisMinimumCadenceRpm = value is int axis ? axis : null;
            map.Invalidate();
        });

    public static readonly BindableProperty AxisMaximumCadenceRpmProperty = BindableProperty.Create(
        nameof(AxisMaximumCadenceRpm),
        typeof(int?),
        typeof(PowerCadenceMap),
        defaultValue: null,
        propertyChanged: static (bindable, _, value) =>
        {
            var map = (PowerCadenceMap)bindable;
            map.drawable.AxisMaximumCadenceRpm = value is int axis ? axis : null;
            map.Invalidate();
        });

    public static readonly BindableProperty AxisMaximumWattsProperty = BindableProperty.Create(
        nameof(AxisMaximumWatts),
        typeof(double?),
        typeof(PowerCadenceMap),
        defaultValue: null,
        propertyChanged: static (bindable, _, value) =>
        {
            var map = (PowerCadenceMap)bindable;
            map.drawable.AxisMaximumWatts = value is double axis ? axis : null;
            map.Invalidate();
        });

    private readonly PowerCadenceDrawable drawable = new();
    private IDispatcherTimer? animationTimer;
    private Page? hostingPage;
    private Window? hostingWindow;
    private bool isPageVisible;
    private bool isWindowRunning;
    private long previousFrameTimestamp;

    public PowerCadenceMap()
    {
        Drawable = drawable;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public double SpeedKilometersPerHour
    {
        get => (double)GetValue(SpeedKilometersPerHourProperty);
        set => SetValue(SpeedKilometersPerHourProperty, value);
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

    public int? AxisMinimumCadenceRpm
    {
        get => (int?)GetValue(AxisMinimumCadenceRpmProperty);
        set => SetValue(AxisMinimumCadenceRpmProperty, value);
    }

    public int? AxisMaximumCadenceRpm
    {
        get => (int?)GetValue(AxisMaximumCadenceRpmProperty);
        set => SetValue(AxisMaximumCadenceRpmProperty, value);
    }

    public double? AxisMaximumWatts
    {
        get => (double?)GetValue(AxisMaximumWattsProperty);
        set => SetValue(AxisMaximumWattsProperty, value);
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == IsVisibleProperty.PropertyName)
        {
            UpdateAnimationState();
        }
    }

    private void OnLoaded(object? sender, EventArgs eventArgs)
    {
        if (animationTimer is not null)
        {
            return;
        }

        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is Page page)
            {
                hostingPage = page;
                page.Appearing += OnPageAppearing;
                page.Disappearing += OnPageDisappearing;
                break;
            }
        }

        hostingWindow = Window;
        if (hostingWindow is not null)
        {
            hostingWindow.Stopped += OnWindowStopped;
            hostingWindow.Resumed += OnWindowResumed;
        }

        isPageVisible = true;
        isWindowRunning = true;
        animationTimer = Dispatcher.CreateTimer();
        animationTimer.Interval = TimeSpan.FromSeconds(1d / 30d);
        animationTimer.Tick += OnAnimationFrame;
        UpdateAnimationState();
    }

    private void OnUnloaded(object? sender, EventArgs eventArgs)
    {
        if (animationTimer is not null)
        {
            animationTimer.Stop();
            animationTimer.Tick -= OnAnimationFrame;
            animationTimer = null;
        }

        if (hostingPage is not null)
        {
            hostingPage.Appearing -= OnPageAppearing;
            hostingPage.Disappearing -= OnPageDisappearing;
            hostingPage = null;
        }

        if (hostingWindow is not null)
        {
            hostingWindow.Stopped -= OnWindowStopped;
            hostingWindow.Resumed -= OnWindowResumed;
            hostingWindow = null;
        }
    }

    private void OnPageAppearing(object? sender, EventArgs eventArgs)
    {
        isPageVisible = true;
        UpdateAnimationState();
    }

    private void OnPageDisappearing(object? sender, EventArgs eventArgs)
    {
        isPageVisible = false;
        UpdateAnimationState();
    }

    private void OnWindowStopped(object? sender, EventArgs eventArgs)
    {
        isWindowRunning = false;
        UpdateAnimationState();
    }

    private void OnWindowResumed(object? sender, EventArgs eventArgs)
    {
        isWindowRunning = true;
        UpdateAnimationState();
    }

    private void UpdateAnimationState()
    {
        if (animationTimer is null)
        {
            return;
        }

        if (isPageVisible && isWindowRunning && IsVisible && drawable.SpeedKilometersPerHour > 0d)
        {
            if (!animationTimer.IsRunning)
            {
                previousFrameTimestamp = Stopwatch.GetTimestamp();
                animationTimer.Start();
            }
        }
        else
        {
            animationTimer.Stop();
        }
    }

    private void OnAnimationFrame(object? sender, EventArgs eventArgs)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(previousFrameTimestamp, now);
        previousFrameTimestamp = now;
        // A delayed UI frame should not turn into a large visual jump after navigation or suspension.
        drawable.Starfield.Advance(elapsed > TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : elapsed, drawable.SpeedKilometersPerHour);
        Invalidate();
    }

    private sealed class PowerCadenceDrawable : IDrawable
    {
        private const float Inset = 5;
        private const float VerticalLabelWidth = 28;
        private const float HorizontalLabelHeight = 13;
        private static readonly Color PlotBackgroundColor = Color.FromArgb("F3F7F7");
        private static readonly Color StarColor = Color.FromArgb("A2A9B0");

        public WarpStarfield Starfield { get; } = new();

        public double SpeedKilometersPerHour { get; set; }

        public IReadOnlyList<PowerCadenceSample> Points { get; set; } = [];

        public Color AccentColor { get; set; } = Colors.Teal;

        public bool HighContrast { get; set; }

        public double? TargetPowerWatts { get; set; }

        public int? MinimumCadenceRpm { get; set; }

        public int? MaximumCadenceRpm { get; set; }

        public int? AxisMinimumCadenceRpm { get; set; }

        public int? AxisMaximumCadenceRpm { get; set; }

        public double? AxisMaximumWatts { get; set; }

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

            DrawStarfield(canvas, plot);
            if (Points.Count == 0
                && (TargetPowerWatts is not double targetPower || targetPower <= 0))
            {
                return;
            }

            // A fixed workout axis keeps the scale steady; otherwise the axis follows the recent samples.
            var hasFixedCadenceAxis = AxisMinimumCadenceRpm is not null && AxisMaximumCadenceRpm is not null;
            var minimumCadence = AxisMinimumCadenceRpm is int axisMinimum && hasFixedCadenceAxis
                ? axisMinimum
                : Points.Count > 0
                    ? Points.Min(point => point.CadenceRpm)
                    : MinimumCadenceRpm ?? (MaximumCadenceRpm is int maximum ? maximum - 10 : 80);
            var maximumCadence = AxisMaximumCadenceRpm is int axisMaximum && hasFixedCadenceAxis
                ? axisMaximum
                : Points.Count > 0
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
            var maximumObservedPower = AxisMaximumWatts is double axisWatts
                ? axisWatts * 1.25d
                : Points.Count > 0 ? Points.Max(point => point.PowerWatts) : 0d;
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
                // ERG mode holds power, so the rider can only move vertically; the cadence band spans every power.
                var zoneMinimumCadence = MinimumCadenceRpm ?? minimumCadence;
                var zoneMaximumCadence = MaximumCadenceRpm ?? maximumCadence;
                var zoneTop = ToCadenceY(zoneMaximumCadence, minimumCadence, maximumCadence, plot);
                var zoneBottom = ToCadenceY(zoneMinimumCadence, minimumCadence, maximumCadence, plot);
                var targetZone = new RectF(plot.Left, zoneTop, plot.Width, zoneBottom - zoneTop);
                canvas.FillColor = Color.FromArgb(HighContrast ? "5571D6A8" : "3371D6A8");
                canvas.FillRectangle(targetZone);
                canvas.StrokeColor = Color.FromArgb(HighContrast ? "147D54" : "428B69");
                canvas.StrokeSize = HighContrast ? 1.5f : 1f;
                canvas.DrawRectangle(targetZone);
                var targetX = ToPowerX(powerTarget, maximumPower, plot);
                canvas.StrokeDashPattern = [4f, 3f];
                canvas.DrawLine(targetX, plot.Top, targetX, plot.Bottom);
                canvas.StrokeDashPattern = null;
            }

            DrawAxisTitles(canvas, plot, HighContrast);
            canvas.FillColor = AccentColor;
            for (var index = 0; index < Points.Count - 1; index++)
            {
                var point = Points[index];
                var freshness = (index + 1f) / Points.Count;
                canvas.Alpha = (HighContrast ? 0.35f : 0.2f) + (freshness * 0.45f);
                canvas.FillCircle(
                    ToPowerX(point.PowerWatts, maximumPower, plot),
                    ToCadenceY(point.CadenceRpm, minimumCadence, maximumCadence, plot),
                    HighContrast ? 2.75f : 2f);
            }

            canvas.Alpha = 1f;
            if (Points.Count == 0)
            {
                return;
            }

            var latest = Points[^1];
            var latestX = ToPowerX(latest.PowerWatts, maximumPower, plot);
            var latestY = ToCadenceY(latest.CadenceRpm, minimumCadence, maximumCadence, plot);
            var isInTarget = (MinimumCadenceRpm is not int cadenceMinimum || latest.CadenceRpm >= cadenceMinimum)
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

        private void DrawStarfield(ICanvas canvas, RectF plot)
        {
            canvas.SaveState();
            canvas.ClipRectangle(plot);
            canvas.FillColor = PlotBackgroundColor;
            canvas.FillRectangle(plot);
            canvas.FillColor = StarColor;
            canvas.StrokeColor = StarColor;
            var trailDepth = WarpStarfield.DepthRatePerSecond(SpeedKilometersPerHour) * 0.12d;
            foreach (var star in Starfield.Stars)
            {
                var x = plot.Center.X + (float)(star.X / star.Depth) * plot.Width / 2f;
                var y = plot.Center.Y + (float)(star.Y / star.Depth) * plot.Height / 2f;
                var tailDepth = double.Min(1d, star.Depth + trailDepth);
                var tailX = plot.Center.X + (float)(star.X / tailDepth) * plot.Width / 2f;
                var tailY = plot.Center.Y + (float)(star.Y / tailDepth) * plot.Height / 2f;
                var proximity = (float)(1d - star.Depth);
                canvas.Alpha = (HighContrast ? 0.12f : 0.35f) + (proximity * 0.45f);
                canvas.StrokeSize = 0.7f + proximity;
                if (trailDepth > 0d)
                {
                    canvas.DrawLine(tailX, tailY, x, y);
                }

                canvas.FillCircle(x, y, 0.6f + proximity);
            }

            canvas.RestoreState();
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
                var cadence = maximumCadence - ((maximumCadence - minimumCadence) * fraction);
                var power = maximumPower * fraction;
                canvas.DrawLine(plot.Left, plot.Top + horizontalOffset, plot.Right, plot.Top + horizontalOffset);
                canvas.DrawLine(plot.Left + verticalOffset, plot.Top, plot.Left + verticalOffset, plot.Bottom);
                canvas.DrawString(
                    cadence.ToString("F0", CultureInfo.InvariantCulture),
                    new RectF(Inset, plot.Top + horizontalOffset - 6, VerticalLabelWidth - 3, 12),
                    HorizontalAlignment.Right,
                    VerticalAlignment.Center);
                canvas.DrawString(
                    power.ToString("F0", CultureInfo.InvariantCulture),
                    new RectF(plot.Left + verticalOffset - 15, plot.Bottom + 1, 30, HorizontalLabelHeight),
                    HorizontalAlignment.Center,
                    VerticalAlignment.Center);
            }
        }

        private static void DrawAxisTitles(ICanvas canvas, RectF plot, bool highContrast)
        {
            canvas.FontColor = Color.FromArgb(highContrast ? "33433D" : "66746D");
            canvas.FontSize = 9;
            canvas.DrawString("cadence rpm ↑ (you)", new RectF(plot.Left + 4, plot.Top + 2, plot.Width / 2, 12), HorizontalAlignment.Left, VerticalAlignment.Top);
            canvas.DrawString("power W → (bike, dashed = target)", new RectF(plot.Center.X, plot.Bottom - 14, (plot.Width / 2) - 4, 12), HorizontalAlignment.Right, VerticalAlignment.Bottom);
        }

        // Samples outside a fixed axis are pinned to the plot edge so the current dot stays visible.
        private static float ToPowerX(double power, double maximumPower, RectF plot) =>
            float.Clamp(plot.Left + (float)(power / maximumPower * plot.Width), plot.Left, plot.Right);

        private static float ToCadenceY(double cadence, double minimumCadence, double maximumCadence, RectF plot) =>
            float.Clamp(plot.Bottom - (float)((cadence - minimumCadence) / (maximumCadence - minimumCadence) * plot.Height), plot.Top, plot.Bottom);
    }
}