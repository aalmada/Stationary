using System.Diagnostics;
using System.Reactive.Linq;

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
        Observable.FromEventPattern(
                handler => window.Destroying += handler,
                handler => window.Destroying -= handler)
            .Take(1)
            .SelectMany(_ => Observable.FromAsync(async () =>
            {
                appShell.Dispose();
                await viewModel.DisposeAsync();
            }))
            .Subscribe(static _ => { }, exception => Trace.TraceError("Dashboard shutdown failed: {0}", exception));
        return window;
    }
}