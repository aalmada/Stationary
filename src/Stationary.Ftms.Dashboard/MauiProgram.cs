using ReactiveUI;

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
        // The app uses only core ReactiveUI; skip probing optional platform assemblies such as ReactiveUI.Maui.
        PlatformRegistrationManager.SetRegistrationNamespaces(RegistrationNamespace.None);

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

#if MACCATALYST
        ButtonHandler.Mapper.AppendToMapping(nameof(IView.Background), ApplyMacCatalystButtonAppearance);
        ButtonHandler.Mapper.AppendToMapping(nameof(IView.IsEnabled), ApplyMacCatalystButtonAppearance);
        ButtonHandler.Mapper.AppendToMapping(nameof(ITextStyle.TextColor), ApplyMacCatalystButtonAppearance);
        ButtonHandler.Mapper.AppendToMapping(nameof(IText.Text), ApplyMacCatalystButtonAppearance);
#endif

        builder.Services.AddSingleton<IFtmsDiscoveryService, PluginBleFtmsDiscoveryService>();
        builder.Services.AddSingleton<IHeartRateDiscoveryService, PluginBleHeartRateDiscoveryService>();
        builder.Services.AddSingleton<IDashboardPlatform, MauiDashboardPlatform>();
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<TodayPage>();
        builder.Services.AddSingleton<RidePage>();
        builder.Services.AddSingleton<ResultsPage>();
        builder.Services.AddSingleton<SensorsPage>();
        builder.Services.AddSingleton<HeartRateSensorPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<TrainingWorkoutsPage>();
        builder.Services.AddSingleton<TrainingPlansPage>();
        builder.Services.AddSingleton<TrainingAssessmentPage>();
        builder.Services.AddSingleton<AppShell>();

        return builder.Build();
    }

#if MACCATALYST
    // A disabled native Catalyst button draws its title in the system disabled color, ignoring the configured
    // color, so disabled titles became unreadable once the window was active. Keep the native control enabled,
    // block its input, and let the style's disabled colors show the state.
    private static void ApplyMacCatalystButtonAppearance(IButtonHandler handler, IButton button)
    {
        var platformButton = handler.PlatformView;
        var configuration = UIButtonConfiguration.FilledButtonConfiguration;
        if (button.Background is SolidPaint { Color: not null } background)
        {
            var backgroundColor = background.Color.ToPlatform();
            configuration.BaseBackgroundColor = backgroundColor;
            var backgroundConfiguration = configuration.Background;
            backgroundConfiguration.BackgroundColor = backgroundColor;
            configuration.Background = backgroundConfiguration;
        }

        if (button is ITextStyle { TextColor: { } textColor })
        {
            configuration.BaseForegroundColor = textColor.ToPlatform();
        }

        // A configured UIButton draws the configuration title and ignores SetTitle, so carry the text over.
        configuration.Title = button is IText { Text: { } text } ? text : platformButton.CurrentTitle;
        platformButton.Configuration = configuration;
        platformButton.Enabled = true;
        platformButton.UserInteractionEnabled = button.IsEnabled;
        platformButton.AccessibilityTraits = button.IsEnabled
            ? platformButton.AccessibilityTraits & ~UIAccessibilityTrait.NotEnabled
            : platformButton.AccessibilityTraits | UIAccessibilityTrait.NotEnabled;
    }
#endif
}