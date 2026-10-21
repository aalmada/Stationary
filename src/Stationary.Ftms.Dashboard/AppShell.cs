using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;

using ReactiveUI;

using Stationary.Ftms.Dashboard.ViewModels;

using IosPage = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page;
using IosPlatform = Microsoft.Maui.Controls.PlatformConfiguration.iOS;
using IosPresentationStyle = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle;

namespace Stationary.Ftms.Dashboard;

public sealed class AppShell : Shell
{
    private readonly CompositeDisposable navigationSubscriptions = [];

    public const string TodayRoute = "today";
    public const string RideRoute = "ride";
    public const string ResultsRoute = "results";
    public const string TrainingRoute = "training";

    private readonly SensorsPage sensorsPage;
    private readonly HeartRateSensorPage heartRateSensorPage;
    private readonly SettingsPage settingsPage;

    public AppShell(TodayPage todayPage, RidePage ridePage, ResultsPage resultsPage, SensorsPage sensorsPage, HeartRateSensorPage heartRateSensorPage, SettingsPage settingsPage, IServiceProvider services, DashboardViewModel viewModel)
    {
        this.sensorsPage = sensorsPage;
        this.heartRateSensorPage = heartRateSensorPage;
        this.settingsPage = settingsPage;
        PrepareSheet(sensorsPage);
        PrepareSheet(heartRateSensorPage);
        PrepareSheet(settingsPage);

        // Pages carry their own headers and in-context navigation; the ride cockpit gets the whole window.
        FlyoutBehavior = FlyoutBehavior.Disabled;

        Items.Add(CreateItem("Home", TodayRoute, todayPage));
        Items.Add(CreateItem("Ride", RideRoute, ridePage));
        Items.Add(CreateLazyItem("Workouts", TrainingRoute, () => HideNavigationBar(services.GetRequiredService<TrainingPage>())));
        Items.Add(CreateItem("Results", ResultsRoute, resultsPage));

        MenuBarItems.Add(CreateDeviceMenu(viewModel));
        MenuBarItems.Add(CreateViewMenu(viewModel));
        MenuBarItems.Add(CreateDisplayMenu(viewModel));

        navigationSubscriptions.Add(Observable.Merge(
                viewModel.WhenAnyValue(viewModel => viewModel.IsWorkoutActive).Skip(1),
                viewModel.Training.WhenAnyValue(training => training.IsActive).Skip(1))
            .Where(static started => started)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(started => { _ = NavigateToDestinationAsync(DashboardDestination.Ride); }));
        navigationSubscriptions.Add(viewModel.NavigationRequests
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(destination => { _ = NavigateToDestinationAsync(destination); }));
        navigationSubscriptions.Add(viewModel.SheetDismissRequests
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(dismiss => { _ = CloseSheetAsync(); }));
    }

    private static void PrepareSheet(ContentPage page) =>
        IosPage.SetModalPresentationStyle(page.On<IosPlatform>(), IosPresentationStyle.FormSheet);

    private static ContentPage HideNavigationBar(ContentPage page)
    {
        SetNavBarIsVisible(page, false);
        return page;
    }

    private static FlyoutItem CreateItem(string title, string route, ContentPage page)
    {
        var item = new FlyoutItem
        {
            Title = title,
            Route = route,
        };
        item.Items.Add(new ShellContent
        {
            Title = title,
            Route = GetContentRoute(route),
            Content = HideNavigationBar(page),
        });
        return item;
    }

    private static FlyoutItem CreateLazyItem(string title, string route, Func<ContentPage> pageFactory)
    {
        var item = new FlyoutItem
        {
            Title = title,
            Route = route,
        };
        item.Items.Add(new ShellContent
        {
            Title = title,
            Route = GetContentRoute(route),
            ContentTemplate = new DataTemplate(pageFactory),
        });
        return item;
    }

    private MenuBarItem CreateDeviceMenu(DashboardViewModel viewModel)
    {
        var menu = new MenuBarItem { Text = "Device" };
        menu.Add(CreateSensorScanItem("Find Fitness Machine", sensorsPage, viewModel.ScanCommand));
        menu.Add(CreateSensorScanItem("Find Heart-Rate Sensor", heartRateSensorPage, viewModel.ScanHeartRateCommand));
        menu.Add(CreateNavigationItem("Fitness Machine", viewModel.ShowSensorsCommand, "D"));
        menu.Add(CreateNavigationItem("Heart-Rate Sensor", viewModel.ShowHeartRateSensorCommand, "H"));
        menu.Add(new MenuFlyoutSeparator());
        menu.Add(new MenuFlyoutItem
        {
            Text = "Disconnect Fitness Machine",
            Command = viewModel.DisconnectCommand,
        });
        menu.Add(new MenuFlyoutItem
        {
            Text = "Disconnect Heart-Rate Sensor",
            Command = viewModel.DisconnectHeartRateCommand,
        });
        return menu;
    }

    private static MenuBarItem CreateViewMenu(DashboardViewModel viewModel)
    {
        var menu = new MenuBarItem { Text = "Go" };
        menu.Add(CreateNavigationItem("Home", viewModel.ShowTodayCommand, "1"));
        menu.Add(CreateNavigationItem("Ride Dashboard", viewModel.ShowRideCommand, "2"));
        menu.Add(CreateNavigationItem("Workouts", viewModel.ShowTrainingCommand, "3"));
        menu.Add(CreateNavigationItem("Results", viewModel.ShowResultsCommand, "4"));
        menu.Add(new MenuFlyoutSeparator());
        menu.Add(CreateNavigationItem("Settings", viewModel.ShowSettingsCommand, ","));
        return menu;
    }

    private static MenuBarItem CreateDisplayMenu(DashboardViewModel viewModel)
    {
        var menu = new MenuBarItem { Text = "Display" };
        menu.Add(new MenuFlyoutItem
        {
            Text = "Toggle Metric Units",
            Command = CreateToggleCommand(viewModel.Telemetry.SetMetricUnitsCommand, () => viewModel.Telemetry.UseMetricUnits),
        });
        menu.Add(new MenuFlyoutItem
        {
            Text = "Toggle Large Values",
            Command = CreateToggleCommand(viewModel.Telemetry.SetLargeTelemetryTextCommand, () => viewModel.Telemetry.UseLargeTelemetryText),
        });
        menu.Add(new MenuFlyoutItem
        {
            Text = "Toggle High Contrast",
            Command = CreateToggleCommand(viewModel.Telemetry.SetHighContrastTelemetryCommand, () => viewModel.Telemetry.UseHighContrastTelemetry),
        });
        return menu;
    }

    private static ICommand CreateToggleCommand(ICommand setValueCommand, Func<bool> getCurrentValue) =>
        new Command(() => setValueCommand.Execute(!getCurrentValue()));

    private static MenuFlyoutItem CreateNavigationItem(string text, ICommand command, string key)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Command = command,
        };
        item.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = KeyboardAcceleratorModifiers.Cmd, Key = key });
        return item;
    }

    private MenuFlyoutItem CreateSensorScanItem(string text, ContentPage sheet, ICommand command) => new()
    {
        Text = text,
        Command = new Command(async () => await ShowSheetAndExecuteAsync(sheet, command)),
    };

    private async Task ShowSheetAndExecuteAsync(ContentPage sheet, ICommand command)
    {
        await ShowSheetAsync(sheet);
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private async Task NavigateToDestinationAsync(DashboardDestination destination)
    {
        var sheet = destination switch
        {
            DashboardDestination.Sensors => sensorsPage,
            DashboardDestination.HeartRateSensor => heartRateSensorPage,
            DashboardDestination.Settings => settingsPage,
            _ => (ContentPage?)null,
        };
        if (sheet is not null)
        {
            await ShowSheetAsync(sheet);
            return;
        }

        var route = destination switch
        {
            DashboardDestination.Ride => RideRoute,
            DashboardDestination.Training => TrainingRoute,
            DashboardDestination.Results => ResultsRoute,
            _ => TodayRoute,
        };

        await CloseSheetAsync();
        if (CurrentItem?.Route == route)
        {
            return;
        }

        try
        {
            await GoToAsync(GetAbsoluteRoute(route));
        }
        catch (Exception)
        {
            // A newer route request can supersede this fire-and-forget navigation.
        }
    }

    private async Task ShowSheetAsync(ContentPage sheet)
    {
        var modalStack = Navigation.ModalStack;
        if (modalStack.Count > 0 && ReferenceEquals(modalStack[^1], sheet))
        {
            return;
        }

        try
        {
            await CloseSheetAsync();
            await Navigation.PushModalAsync(sheet);
        }
        catch (Exception)
        {
            // A newer sheet request can supersede this fire-and-forget presentation.
        }
    }

    private async Task CloseSheetAsync()
    {
        if (Navigation.ModalStack.Count == 0)
        {
            return;
        }

        try
        {
            await Navigation.PopModalAsync();
        }
        catch (Exception)
        {
            // The sheet may already be dismissing.
        }
    }

    private static string GetAbsoluteRoute(string route) => $"//{route}/{GetContentRoute(route)}";

    private static string GetContentRoute(string route) => $"{route}-content";
}