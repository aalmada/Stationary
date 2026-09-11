using Microsoft.Extensions.DependencyInjection;

using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public partial class App : Application
{
    private readonly AppShell appShell;
    private readonly DashboardViewModel viewModel;

    public App(IServiceProvider services, DashboardViewModel viewModel)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
        appShell = services.GetRequiredService<AppShell>();
        this.viewModel = viewModel;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(appShell);
        window.Destroying += OnWindowDestroying;
        return window;
    }

    private async void OnWindowDestroying(object? sender, EventArgs eventArgs)
    {
        if (sender is Window window)
        {
            window.Destroying -= OnWindowDestroying;
        }

        await viewModel.DisposeAsync();
    }
}