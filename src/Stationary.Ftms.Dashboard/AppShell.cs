using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;

using ReactiveUI;

using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public sealed class AppShell : Shell
{
    private readonly CompositeDisposable navigationSubscriptions = [];

    public const string TodayRoute = "today";
    public const string RideRoute = "ride";
    public const string ResultsRoute = "results";
    public const string SensorsRoute = "sensors";
    public const string SettingsRoute = "settings";
    public const string TrainingRoute = "training";

    public AppShell(TodayPage todayPage, RidePage ridePage, ResultsPage resultsPage, SensorsPage sensorsPage, SettingsPage settingsPage, IServiceProvider services, DashboardViewModel viewModel)
    {
        FlyoutBehavior = FlyoutBehavior.Locked;
        FlyoutWidth = 180;

        Items.Add(CreateItem("Today", TodayRoute, todayPage));
        Items.Add(CreateItem("Sensors", SensorsRoute, sensorsPage));
        Items.Add(CreateItem("Ride", RideRoute, ridePage));
        Items.Add(CreateLazyItem("Training", TrainingRoute, () => services.GetRequiredService<TrainingPage>()));
        Items.Add(CreateItem("Results", ResultsRoute, resultsPage));
        Items.Add(CreateItem("Settings", SettingsRoute, settingsPage));

        MenuBarItems.Add(CreateDeviceMenu(viewModel));
        MenuBarItems.Add(CreateViewMenu(viewModel));
        MenuBarItems.Add(CreateDisplayMenu(viewModel));

        navigationSubscriptions.Add(viewModel.WhenAnyValue(viewModel => viewModel.IsConnected)
            .Skip(1)
            .Where(static connected => connected)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(connected => { _ = NavigateToRideAfterConnectingAsync(); }));
        navigationSubscriptions.Add(viewModel.Training.WhenAnyValue(training => training.HasStartedRide)
            .Skip(1)
            .Where(static started => started)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(started => { _ = NavigateToRideAfterTrainingStartsAsync(); }));
        navigationSubscriptions.Add(viewModel.NavigationRequests
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(destination => { _ = NavigateToDestinationAsync(destination); }));
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
            Content = page,
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
        menu.Add(CreateSensorScanItem("Find Fitness Machine", SensorsRoute, viewModel.ScanCommand));
        menu.Add(CreateSensorScanItem("Add Heart-Rate Sensor", SensorsRoute, viewModel.ScanHeartRateCommand));
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
        var menu = new MenuBarItem { Text = "View" };
        menu.Add(CreateNavigationItem("Today", viewModel.ShowTodayCommand));
        menu.Add(CreateNavigationItem("Ride", viewModel.ShowRideCommand));
        menu.Add(CreateNavigationItem("Training", viewModel.ShowTrainingCommand));
        menu.Add(CreateNavigationItem("Results", viewModel.ShowResultsCommand));
        menu.Add(CreateNavigationItem("Sensors", viewModel.ShowSensorsCommand));
        menu.Add(CreateNavigationItem("Settings", viewModel.ShowSettingsCommand));
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

    private static MenuFlyoutItem CreateNavigationItem(string text, ICommand command) => new()
    {
        Text = text,
        Command = command,
    };

    private MenuFlyoutItem CreateSensorScanItem(string text, string route, ICommand command) => new()
    {
        Text = text,
        Command = new Command(async () => await NavigateAndExecuteAsync(route, command)),
    };

    private async Task NavigateAndExecuteAsync(string route, ICommand command)
    {
        try
        {
            await GoToAsync(GetAbsoluteRoute(route));
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
        catch (Exception)
        {
        }
    }

    private async Task NavigateToRideAfterConnectingAsync()
    {
        if (CurrentItem?.Route != SensorsRoute)
        {
            return;
        }

        try
        {
            await GoToAsync(GetAbsoluteRoute(RideRoute));
        }
        catch (Exception)
        {
        }
    }

    private async Task NavigateToRideAfterTrainingStartsAsync()
    {
        if (CurrentItem?.Route == RideRoute)
        {
            return;
        }

        try
        {
            await GoToAsync(GetAbsoluteRoute(RideRoute));
        }
        catch (Exception)
        {
        }
    }

    private async Task NavigateToDestinationAsync(DashboardDestination destination)
    {
        var route = destination switch
        {
            DashboardDestination.Today => TodayRoute,
            DashboardDestination.Ride => RideRoute,
            DashboardDestination.Training => TrainingRoute,
            DashboardDestination.Results => ResultsRoute,
            DashboardDestination.Settings => SettingsRoute,
            DashboardDestination.Sensors => SensorsRoute,
            _ => TodayRoute,
        };

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

    private static string GetAbsoluteRoute(string route) => $"//{route}/{GetContentRoute(route)}";

    private static string GetContentRoute(string route) => $"{route}-content";
}