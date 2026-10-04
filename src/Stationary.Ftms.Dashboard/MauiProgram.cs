using Stationary.Ftms.Dashboard.Services;
using Stationary.Ftms.Dashboard.ViewModels;

#if MACCATALYST
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

using UIKit;
#endif

namespace Stationary.Ftms.Dashboard;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

#if MACCATALYST
        ButtonHandler.Mapper.AppendToMapping(nameof(IView.Background), ApplyMacCatalystButtonBackground);
        ButtonHandler.Mapper.AppendToMapping(nameof(IView.IsEnabled), ApplyMacCatalystButtonBackground);
#endif

        builder.Services.AddSingleton<IFtmsDiscoveryService, PluginBleFtmsDiscoveryService>();
        builder.Services.AddSingleton<IHeartRateDiscoveryService, PluginBleHeartRateDiscoveryService>();
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<TodayPage>();
        builder.Services.AddSingleton<RidePage>();
        builder.Services.AddSingleton<ResultsPage>();
        builder.Services.AddSingleton<SensorsPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<TrainingPage>();
        builder.Services.AddSingleton<AppShell>();

        return builder.Build();
    }

#if MACCATALYST
    private static void ApplyMacCatalystButtonBackground(IButtonHandler handler, IButton button)
    {
        var configuration = UIButtonConfiguration.FilledButtonConfiguration;

        if (button.Background is SolidPaint { Color: not null } background)
        {
            var backgroundColor = background.Color.ToPlatform();
            configuration.BaseBackgroundColor = backgroundColor;
            var backgroundConfiguration = configuration.Background;
            backgroundConfiguration.BackgroundColor = backgroundColor;
            configuration.Background = backgroundConfiguration;
        }

        handler.PlatformView.Configuration = configuration;
    }
#endif
}