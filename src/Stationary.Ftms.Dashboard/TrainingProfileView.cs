using Microsoft.Maui.Graphics;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard;

public sealed class TrainingProfileView : GraphicsView
{
    public static readonly BindableProperty SegmentsProperty = BindableProperty.Create(
        nameof(Segments),
        typeof(IReadOnlyList<TrainingSegment>),
        typeof(TrainingProfileView),
        Array.Empty<TrainingSegment>(),
        propertyChanged: static (bindable, _, _) => ((TrainingProfileView)bindable).Invalidate());

    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress),
        typeof(double?),
        typeof(TrainingProfileView),
        null,
        propertyChanged: static (bindable, _, _) => ((TrainingProfileView)bindable).Invalidate());

    public TrainingProfileView()
    {
        Drawable = new TrainingProfileDrawable(this);
    }

    public IReadOnlyList<TrainingSegment> Segments
    {
        get => (IReadOnlyList<TrainingSegment>)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double? Progress
    {
        get => (double?)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    private sealed class TrainingProfileDrawable(TrainingProfileView profile) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var segments = profile.Segments;
            if (segments.Count == 0 || dirtyRect.Width <= 0f || dirtyRect.Height <= 0f)
            {
                return;
            }

            var totalSeconds = 0d;
            foreach (var segment in segments)
            {
                totalSeconds += segment.Duration.TotalSeconds;
            }

            if (totalSeconds <= 0d)
            {
                return;
            }

            var horizontalOffset = dirtyRect.X;
            foreach (var segment in segments)
            {
                var segmentWidth = (float)(segment.Duration.TotalSeconds / totalSeconds * dirtyRect.Width);
                var relativeHeight = float.Clamp((float)(segment.FunctionalThresholdPowerPercentage / 120d), 0.14f, 1f);
                var barHeight = dirtyRect.Height * relativeHeight;
                canvas.FillColor = GetTargetColor(segment.FunctionalThresholdPowerPercentage);
                canvas.FillRectangle(horizontalOffset, dirtyRect.Bottom - barHeight, segmentWidth, barHeight);
                horizontalOffset += segmentWidth;
            }

            if (profile.Progress is double progress)
            {
                var cursorX = dirtyRect.X + (float)(double.Clamp(progress, 0d, 1d) * dirtyRect.Width);
                canvas.FillColor = Colors.White.WithAlpha(0.55f);
                canvas.FillRectangle(dirtyRect.X, dirtyRect.Y, cursorX - dirtyRect.X, dirtyRect.Height);
                canvas.StrokeColor = Color.FromArgb("#112D35");
                canvas.StrokeSize = 3f;
                canvas.DrawLine(cursorX, dirtyRect.Y, cursorX, dirtyRect.Bottom);
            }
        }

        private static Color GetTargetColor(double percentage) => percentage switch
        {
            <= 60d => Color.FromArgb("#147D70"),
            <= 75d => Color.FromArgb("#46A36A"),
            <= 90d => Color.FromArgb("#C5A23C"),
            <= 105d => Color.FromArgb("#E17D3D"),
            <= 120d => Color.FromArgb("#D9564D"),
            _ => Color.FromArgb("#B63862"),
        };
    }
}