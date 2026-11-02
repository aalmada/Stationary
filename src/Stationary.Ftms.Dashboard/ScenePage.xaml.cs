using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public partial class ScenePage : ContentPage
{
    public const string WindowTitle = "Stationary Ride Scene";

    public ScenePage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel.Scene;
    }

    // Stops the render thread and releases Vulkan resources as soon as the window goes away.
    internal void StopRendering() => SceneView.Handler?.DisconnectHandler();
}