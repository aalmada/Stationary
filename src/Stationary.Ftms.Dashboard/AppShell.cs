using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;

using ReactiveUI;

using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

public sealed class AppShell : Shell
{
    private readonly CompositeDisposable navigationSubscriptions = [];

    public const string ControlRoute = "control";
    public const string RideRoute = "ride";
    public const string SensorsRoute = "sensors";
    public const string SessionRoute = "session";

    public AppShell(RidePage ridePage, ControlPage controlPage, SensorsPage sensorsPage, SessionPage sessionPage, DashboardViewModel viewModel)
    {
        FlyoutBehavior = FlyoutBehavior.Locked;
        FlyoutWidth = 180;

        Items.Add(CreateItem("Sensors", SensorsRoute, sensorsPage));
        Items.Add(CreateItem("Ride", RideRoute, ridePage));
        Items.Add(CreateItem("Control", ControlRoute, controlPage));
        Items.Add(CreateItem("Session", SessionRoute, sessionPage));

        MenuBarItems.Add(CreateDeviceMenu(viewModel));
        MenuBarItems.Add(CreateSessionMenu(viewModel));
        MenuBarItems.Add(CreateViewMenu());
        MenuBarItems.Add(CreateDisplayMenu(viewModel));

        navigationSubscriptions.Add(viewModel.WhenAnyValue(viewModel => viewModel.IsConnected)
            .Skip(1)
            .Where(static connected => connected)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(connected => { _ = NavigateToRideAfterConnectingAsync(); }));
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

    private static MenuBarItem CreateSessionMenu(DashboardViewModel viewModel)
    {
        var menu = new MenuBarItem { Text = "Session" };
        menu.Add(new MenuFlyoutItem
        {
            Text = "Start or Resume",
            Command = viewModel.StartCommand,
        });
        menu.Add(new MenuFlyoutItem
        {
            Text = "Pause",
            Command = viewModel.StopCommand,
        });
        menu.Add(new MenuFlyoutSeparator());
        menu.Add(new MenuFlyoutItem
        {
            Text = "Reset Local Totals",
            Command = viewModel.ResetSessionCommand,
        });
        menu.Add(new MenuFlyoutItem
        {
            Text = "Export CSV",
            Command = viewModel.ExportSessionCommand,
        });
        return menu;
    }

    private MenuBarItem CreateViewMenu()
    {
        var menu = new MenuBarItem { Text = "View" };
        menu.Add(CreateNavigationItem("Ride", RideRoute));
        menu.Add(CreateNavigationItem("Control", ControlRoute));
        menu.Add(CreateNavigationItem("Sensors", SensorsRoute));
        menu.Add(CreateNavigationItem("Session", SessionRoute));
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

    private MenuFlyoutItem CreateNavigationItem(string text, string route) => new()
    {
        Text = text,
        Command = new Command(async () => await GoToAsync(GetAbsoluteRoute(route))),
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

    private static string GetAbsoluteRoute(string route) => $"//{route}/{GetContentRoute(route)}";

    private static string GetContentRoute(string route) => $"{route}-content";
}