using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;

using ReactiveUI;

using Stationary.Ftms.Dashboard.ViewModels;

using IosPage = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page;
using IosPlatform = Microsoft.Maui.Controls.PlatformConfiguration.iOS;
using IosPresentationStyle = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle;

namespace Stationary.Ftms.Dashboard;

public sealed class AppShell : Shell, IDisposable
{
    private readonly CompositeDisposable navigationSubscriptions = [];
    private readonly ReactiveOperationQueue navigationQueue;
    private readonly Action<Exception> reportNavigationError;

    public const string TodayRoute = "today";
    public const string RideRoute = "ride";
    public const string ResultsRoute = "results";
    public const string TrainingRoute = "training";

    private readonly NavigationPage sensorsPage;
    private readonly NavigationPage heartRateSensorPage;
    private readonly NavigationPage settingsPage;
    private readonly DashboardViewModel viewModel;
    private readonly ICommand findFitnessMachineCommand;
    private readonly ICommand findHeartRateSensorCommand;
    private readonly ICommand toggleMetricUnitsCommand;
    private readonly ICommand toggleLargeValuesCommand;
    private readonly ICommand toggleHighContrastCommand;

    public AppShell(TodayPage todayPage, RidePage ridePage, ResultsPage resultsPage, SensorsPage sensorsPage, HeartRateSensorPage heartRateSensorPage, SettingsPage settingsPage, IServiceProvider services, DashboardViewModel viewModel)
    {
        var showNavigationError = ReactiveCommand.CreateFromTask<Exception>(exception =>
            DisplayAlertAsync("Navigation failed", exception.Message, "OK"));
        navigationSubscriptions.Add(showNavigationError.ThrownExceptions.Subscribe(exception =>
        {
            viewModel.ReportUiError(exception);
            Trace.TraceError("Navigation error notification failed: {0}", exception);
        }));
        navigationSubscriptions.Add(showNavigationError);
        reportNavigationError = exception =>
        {
            viewModel.ReportUiError(exception);
            Trace.TraceError("Navigation failed: {0}", exception);
            ((ICommand)showNavigationError).Execute(exception);
        };
        navigationQueue = new(RxApp.MainThreadScheduler, reportNavigationError);
        this.viewModel = viewModel;
        this.sensorsPage = CreateSheet(sensorsPage, viewModel.CloseSheetCommand);
        this.heartRateSensorPage = CreateSheet(heartRateSensorPage, viewModel.CloseSheetCommand);
        this.settingsPage = CreateSheet(settingsPage, viewModel.CloseSheetCommand);
        findFitnessMachineCommand = CreateSensorScanCommand(this.sensorsPage, viewModel.ScanCommand);
        findHeartRateSensorCommand = CreateSensorScanCommand(this.heartRateSensorPage, viewModel.ScanHeartRateCommand);
        toggleMetricUnitsCommand = CreateToggleCommand(viewModel.Telemetry.SetMetricUnitsCommand, () => viewModel.Telemetry.UseMetricUnits);
        toggleLargeValuesCommand = CreateToggleCommand(viewModel.Telemetry.SetLargeTelemetryTextCommand, () => viewModel.Telemetry.UseLargeTelemetryText);
        toggleHighContrastCommand = CreateToggleCommand(viewModel.Telemetry.SetHighContrastTelemetryCommand, () => viewModel.Telemetry.UseHighContrastTelemetry);

        FlyoutBehavior = FlyoutBehavior.Disabled;
        SetBackgroundColor(this, (Color)Application.Current!.Resources["Canvas"]);
        SetForegroundColor(this, (Color)Application.Current.Resources["Accent"]);
        SetTitleColor(this, (Color)Application.Current.Resources["Accent"]);
        SetNavBarHasShadow(this, false);

        Items.Add(CreateItem("Home", TodayRoute, todayPage));
        Items.Add(CreateItem("Ride", RideRoute, WithBackNavigation(ridePage, viewModel.ShowTodayCommand, "Back to home; the ride keeps recording")));
        Items.Add(CreateTabbedItem(
            "Workouts",
            TrainingRoute,
            ("Workouts", "workouts", () => WithBackNavigation(services.GetRequiredService<TrainingWorkoutsPage>(), viewModel.ShowTodayCommand, "Back to home")),
            ("Plans", "plans", () => WithBackNavigation(services.GetRequiredService<TrainingPlansPage>(), viewModel.ShowTodayCommand, "Back to home")),
            ("Assessment", "assessment", () => WithBackNavigation(services.GetRequiredService<TrainingAssessmentPage>(), viewModel.ShowTodayCommand, "Back to home"))));
        Items.Add(CreateItem("Results", ResultsRoute, WithBackNavigation(resultsPage, viewModel.ShowTodayCommand, "Back to home")));

        MenuBarItems.Add(CreateDeviceMenu());
        MenuBarItems.Add(CreateGoMenu());
        MenuBarItems.Add(CreateDisplayMenu());

        navigationSubscriptions.Add(Observable.Merge(
                viewModel.WhenAnyValue(viewModel => viewModel.IsWorkoutActive).Skip(1),
                viewModel.Training.WhenAnyValue(training => training.IsActive).Skip(1))
            .Where(static started => started)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => navigationQueue.Enqueue(_ => NavigateToDestinationAsync(DashboardDestination.Ride)), viewModel.ReportUiError));
        navigationSubscriptions.Add(viewModel.NavigationRequests
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(destination => navigationQueue.Enqueue(_ => NavigateToDestinationAsync(destination)), viewModel.ReportUiError));
        navigationSubscriptions.Add(viewModel.SheetDismissRequests
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => navigationQueue.Enqueue(_ => CloseSheetAsync()), viewModel.ReportUiError));
    }

    public void Dispose()
    {
        navigationSubscriptions.Dispose();
        navigationQueue.Dispose();
    }

    private static NavigationPage CreateSheet(ContentPage page, ICommand command)
    {
        var sheet = new NavigationPage(WithBackNavigation(page, command, $"Close {page.Title}"))
        {
            BarBackgroundColor = (Color)Application.Current!.Resources["Canvas"],
            BarTextColor = (Color)Application.Current.Resources["Accent"],
        };
        IosPage.SetModalPresentationStyle(sheet.On<IosPlatform>(), IosPresentationStyle.FormSheet);
        return sheet;
    }

    private static ContentPage WithBackNavigation(ContentPage page, ICommand command, string description)
    {
        page.ToolbarItems.Add(new ToolbarItem
        {
            Command = command,
            Text = description,
            AutomationId = "NavigateBack",
            Order = ToolbarItemOrder.Primary,
            IconImageSource = new FontImageSource
            {
                Glyph = "\u2190",
                Size = 24,
                Color = (Color)Application.Current!.Resources["Accent"],
            },
        });
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
            Content = page,
        });
        return item;
    }

    // Several contents in one section render as native top tabs; each page is still created only when first shown.
    private static FlyoutItem CreateTabbedItem(string title, string route, params ReadOnlySpan<(string Title, string Route, Func<ContentPage> PageFactory)> tabs)
    {
        var section = new Tab
        {
            Title = title,
            Route = GetContentRoute(route),
        };
        var canvas = (Color)Application.Current!.Resources["Canvas"];
        var accent = (Color)Application.Current.Resources["Accent"];
        var muted = (Color)Application.Current.Resources["Muted"];
        SetBackgroundColor(section, canvas);
        SetForegroundColor(section, accent);
        SetTitleColor(section, accent);
        SetUnselectedColor(section, muted);
        foreach (var tab in tabs)
        {
            section.Items.Add(new ShellContent
            {
                Title = tab.Title,
                Route = tab.Route,
                ContentTemplate = new DataTemplate(tab.PageFactory),
            });
        }

        section.CurrentItem = section.Items[0];
        var item = new FlyoutItem
        {
            Title = title,
            Route = route,
        };
        item.Items.Add(section);
        return item;
    }

    private MenuBarItem CreateDeviceMenu()
    {
        var menu = new MenuBarItem { Text = "Device" };
        menu.Add(new MenuFlyoutItem { Text = "Find Fitness Machine", Command = findFitnessMachineCommand });
        menu.Add(new MenuFlyoutItem { Text = "Find Heart-Rate Sensor", Command = findHeartRateSensorCommand });
        menu.Add(CreateNavigationItem("Fitness Machine", viewModel.ShowSensorsCommand, "D"));
        // Cmd+H is reserved for Hide; UIKit drops the whole menu bar when a menu duplicates a system shortcut.
        menu.Add(CreateNavigationItem("Heart-Rate Sensor", viewModel.ShowHeartRateSensorCommand, "H", KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Shift));
        menu.Add(new MenuFlyoutSeparator());
        menu.Add(new MenuFlyoutItem { Text = "Disconnect Fitness Machine", Command = viewModel.DisconnectCommand });
        menu.Add(new MenuFlyoutItem { Text = "Disconnect Heart-Rate Sensor", Command = viewModel.DisconnectHeartRateCommand });
        return menu;
    }

    private MenuBarItem CreateGoMenu()
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

    private MenuBarItem CreateDisplayMenu()
    {
        var menu = new MenuBarItem { Text = "Display" };
        menu.Add(new MenuFlyoutItem { Text = "Toggle Metric Units", Command = toggleMetricUnitsCommand });
        menu.Add(new MenuFlyoutItem { Text = "Toggle Large Values", Command = toggleLargeValuesCommand });
        menu.Add(new MenuFlyoutItem { Text = "Toggle High Contrast", Command = toggleHighContrastCommand });
        return menu;
    }

    private ICommand CreateToggleCommand(ICommand setValueCommand, Func<bool> getCurrentValue)
    {
        var command = ReactiveCommand.Create(() =>
        {
            var value = !getCurrentValue();
            if (setValueCommand.CanExecute(value))
            {
                setValueCommand.Execute(value);
            }
        });
        navigationSubscriptions.Add(command);
        navigationSubscriptions.Add(command.ThrownExceptions.Subscribe(reportNavigationError));
        return command;
    }

    private static MenuFlyoutItem CreateNavigationItem(string text, ICommand command, string key, KeyboardAcceleratorModifiers modifiers = KeyboardAcceleratorModifiers.Cmd)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Command = command,
        };
        item.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = modifiers, Key = key });
        return item;
    }

    private ICommand CreateSensorScanCommand(NavigationPage sheet, ICommand command)
    {
        var scan = ReactiveCommand.Create(() => navigationQueue.Enqueue(_ => ShowSheetAndExecuteAsync(sheet, command)));
        navigationSubscriptions.Add(scan);
        navigationSubscriptions.Add(scan.ThrownExceptions.Subscribe(reportNavigationError));
        return scan;
    }

    private async Task ShowSheetAndExecuteAsync(NavigationPage sheet, ICommand command)
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
            _ => (NavigationPage?)null,
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

        await GoToAsync(GetAbsoluteRoute(route));
    }

    private async Task ShowSheetAsync(NavigationPage sheet)
    {
        var modalStack = Navigation.ModalStack;
        if (modalStack.Count > 0 && ReferenceEquals(modalStack[^1], sheet))
        {
            return;
        }

        await CloseSheetAsync();
        await Navigation.PushModalAsync(sheet);
    }

    private async Task CloseSheetAsync()
    {
        if (Navigation.ModalStack.Count == 0)
        {
            return;
        }

        await Navigation.PopModalAsync();
    }

    private static string GetAbsoluteRoute(string route) => $"//{route}/{GetContentRoute(route)}";

    private static string GetContentRoute(string route) => $"{route}-content";
}