using CoreAnimation;

using CoreGraphics;

using Microsoft.Maui.Handlers;

using Stationary.Ftms.Dashboard.Scene;

using UIKit;

namespace Stationary.Ftms.Dashboard;

// Hosts the CAMetalLayer MoltenVK presents to. It is a sublayer rather than the backing layer because
// MoltenVK configures the layer from the render thread, and UIKit rejects off-main-thread changes to a backing layer.
public sealed class MetalHostView : UIView
{
    private VulkanSceneRenderer? renderer;

    public MetalHostView()
    {
        Opaque = true;
        BackgroundColor = UIColor.FromRGB(161, 201, 240);
        Layer.AddSublayer(MetalLayer);
    }

    internal CAMetalLayer MetalLayer { get; } = new() { Opaque = true };

    internal void Attach(VulkanSceneRenderer sceneRenderer)
    {
        renderer = sceneRenderer;
        UpdateDrawableSize();
    }

    internal void Detach() => renderer = null;

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        UpdateDrawableSize();
    }

    public override void MovedToWindow()
    {
        base.MovedToWindow();
        UpdateDrawableSize();
    }

    private void UpdateDrawableSize()
    {
        var scale = Window?.Screen.Scale ?? TraitCollection.DisplayScale;
        if (scale <= 0)
        {
            scale = 1;
        }

        CATransaction.Begin();
        CATransaction.DisableActions = true;
        MetalLayer.Frame = Bounds;
        CATransaction.Commit();

        var layer = MetalLayer;
        layer.ContentsScale = scale;
        var width = (uint)double.Max(0, double.Round(Bounds.Width * scale));
        var height = (uint)double.Max(0, double.Round(Bounds.Height * scale));
        if (width > 0 && height > 0)
        {
            layer.DrawableSize = new CGSize(width, height);
        }

        renderer?.Resize(width, height);
    }
}

public sealed class RideSceneViewHandler : ViewHandler<RideSceneView, MetalHostView>
{
    public static readonly IPropertyMapper<RideSceneView, RideSceneViewHandler> PropertyMapper =
        new PropertyMapper<RideSceneView, RideSceneViewHandler>(ViewMapper)
        {
            [nameof(RideSceneView.SceneFrame)] = MapSceneFrame,
        };

    private VulkanSceneRenderer? renderer;

    public RideSceneViewHandler()
        : base(PropertyMapper)
    {
    }

    protected override MetalHostView CreatePlatformView() => [];

    protected override void ConnectHandler(MetalHostView platformView)
    {
        base.ConnectHandler(platformView);
        renderer = new VulkanSceneRenderer(platformView.MetalLayer.Handle);
        renderer.Update(VirtualView.SceneFrame);
        platformView.Attach(renderer);
        renderer.Start();
    }

    protected override void DisconnectHandler(MetalHostView platformView)
    {
        platformView.Detach();
        renderer?.Dispose();
        renderer = null;
        base.DisconnectHandler(platformView);
    }

    private static void MapSceneFrame(RideSceneViewHandler handler, RideSceneView view) => handler.renderer?.Update(view.SceneFrame);
}