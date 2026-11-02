using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard;

// Hosts the Vulkan ride scene. The bound frame is one-way from the view model; the platform handler renders it.
public sealed class RideSceneView : View
{
    public static readonly BindableProperty SceneFrameProperty = BindableProperty.Create(
        nameof(SceneFrame),
        typeof(RideSceneFrame),
        typeof(RideSceneView),
        RideSceneFrame.Idle);

    public RideSceneFrame SceneFrame
    {
        get => (RideSceneFrame)GetValue(SceneFrameProperty);
        set => SetValue(SceneFrameProperty, value);
    }
}