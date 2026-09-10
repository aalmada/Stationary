using Stationary.Ftms.Dashboard.Services;
using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        builder.Services.AddSingleton<IFtmsDiscoveryService, PluginBleFtmsDiscoveryService>();
        builder.Services.AddSingleton<IHeartRateDiscoveryService, PluginBleHeartRateDiscoveryService>();
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<ControlPage>();
        builder.Services.AddSingleton<RidePage>();
        builder.Services.AddSingleton<SensorsPage>();
        builder.Services.AddSingleton<SessionPage>();
        builder.Services.AddSingleton<AppShell>();

        return builder.Build();
    }
}