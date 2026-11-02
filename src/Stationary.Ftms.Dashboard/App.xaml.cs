using System.Diagnostics;
using System.Reactive.Linq;

using Microsoft.Extensions.DependencyInjection;

using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public partial class App : Application
{
    private readonly AppShell appShell;
    private readonly DashboardViewModel viewModel;
    private readonly SceneWindowCoordinator sceneWindows;
    private Window? mainWindow;

    public App(IServiceProvider services, DashboardViewModel viewModel)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
        appShell = services.GetRequiredService<AppShell>();
        this.viewModel = viewModel;
        sceneWindows = new SceneWindowCoordinator(viewModel);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // macOS can restore a ride scene window from the previous launch; the coordinator owns that window, so close
        // the restored one instead of showing the dashboard twice.
        if (mainWindow is not null)
        {
            var restored = new Window(new ContentPage());
            Observable.FromEventPattern(handler => restored.Created += handler, handler => restored.Created -= handler)
                .Take(1)
                .Subscribe(_ => Dispatcher.Dispatch(() => CloseWindow(restored)));
            return restored;
        }

        var window = new Window(appShell);
        mainWindow = window;
        sceneWindows.Start();
        Observable.FromEventPattern(
                handler => window.Destroying += handler,
                handler => window.Destroying -= handler)
            .Take(1)
            .SelectMany(_ => Observable.FromAsync(async () =>
            {
                sceneWindows.Dispose();
                appShell.Dispose();
                await viewModel.DisposeAsync();
            }))
            .Subscribe(static _ => { }, exception => Trace.TraceError("Dashboard shutdown failed: {0}", exception));
        return window;
    }
}