using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;

using ReactiveUI;

using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.ViewModels.Tests;

public sealed class GeneratedCommandTests
{
    static GeneratedCommandTests()
    {
        PlatformRegistrationManager.SetRegistrationNamespaces(RegistrationNamespace.None);
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
    }

    [Test]
    public async Task SettingsCommands_RestoreAndPersistThroughPlatform()
    {
        using var platform = new TestDashboardPlatform();
        platform.SetPreference("MetricUnits", false);
        platform.SetPreference("FunctionalThresholdPower", "250");
        platform.SetPreference("PreferredCadenceRpm", "55");
        var viewModel = new TelemetryPresentationViewModel(platform);
        await Assert.That(viewModel.UseMetricUnits).IsFalse();
        await Assert.That(viewModel.FunctionalThresholdPowerText).IsEqualTo("250");
        await Assert.That(viewModel.PreferredCadenceRpm).IsEqualTo(55);

        await viewModel.SetMetricUnitsCommand.Execute(true).ToTask();
        await viewModel.SetLargeTelemetryTextCommand.Execute(true).ToTask();
        await viewModel.SetHighContrastTelemetryCommand.Execute(true).ToTask();
        await viewModel.SetCompactChartsCommand.Execute(true).ToTask();
        viewModel.PreferredCadenceText = "48";
        await viewModel.ApplyPreferredCadenceCommand.Execute().ToTask();
        var restored = new TelemetryPresentationViewModel(platform);

        await Assert.That(restored.UseMetricUnits).IsTrue();
        await Assert.That(restored.UseLargeTelemetryText).IsTrue();
        await Assert.That(restored.UseHighContrastTelemetry).IsTrue();
        await Assert.That(restored.UseCompactCharts).IsTrue();
        await Assert.That(restored.PreferredCadenceRpm).IsEqualTo(48);
        await Assert.That(viewModel.ShowChartSummaries).IsFalse();
    }

    [Test]
    public async Task PreferredCadenceCommand_RejectsInvalidValuesWithoutOverwritingSavedCadence()
    {
        using var platform = new TestDashboardPlatform();
        var viewModel = new TelemetryPresentationViewModel(platform) { PreferredCadenceText = "39" };

        await viewModel.ApplyPreferredCadenceCommand.Execute().ToTask();

        await Assert.That(viewModel.PreferredCadenceRpm).IsEqualTo(90);
        await Assert.That(viewModel.PreferredCadenceStatusText.Contains("enter a whole number", StringComparison.Ordinal)).IsTrue();
        await Assert.That(platform.GetPreference("PreferredCadenceRpm", "unchanged")).IsEqualTo("unchanged");

        viewModel.PreferredCadenceText = int.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await viewModel.ApplyPreferredCadenceCommand.Execute().ToTask();
        await Assert.That(viewModel.PreferredCadenceRpm).IsEqualTo(90);
    }

    [Test]
    public async Task PreferredCadence_MigratesLegacyCadenceOffset()
    {
        using var platform = new TestDashboardPlatform();
        platform.SetPreference("CadenceTargetOffsetRpm", "-10");

        var viewModel = new TelemetryPresentationViewModel(platform);

        await Assert.That(viewModel.PreferredCadenceRpm).IsEqualTo(80);
    }

    [Test]
    public async Task FtpCommand_SavesValidValueAndClearsInvalidValue()
    {
        using var platform = new TestDashboardPlatform();
        var viewModel = new TelemetryPresentationViewModel(platform) { FunctionalThresholdPowerText = "275" };

        await viewModel.ApplyFunctionalThresholdPowerCommand.Execute().ToTask();
        await Assert.That(platform.GetPreference("FunctionalThresholdPower", string.Empty)).IsEqualTo("275");

        viewModel.FunctionalThresholdPowerText = "invalid";
        await viewModel.ApplyFunctionalThresholdPowerCommand.Execute().ToTask();
        await Assert.That(platform.GetPreference("FunctionalThresholdPower", "removed")).IsEqualTo("removed");
    }

    [Test]
    public async Task SessionDistance_IntegratesVirtualBikeSpeedInsteadOfBikeCounter()
    {
        using var platform = new TestDashboardPlatform();
        var viewModel = new TelemetryPresentationViewModel(platform);
        var start = DateTimeOffset.UnixEpoch;

        viewModel.Present(CreatePowerBikeSample(start, 0));
        viewModel.Present(CreatePowerBikeSample(start.AddSeconds(1), 1_000));

        using (Assert.Multiple())
        {
            await Assert.That(viewModel.SpeedText).IsEqualTo($"{VirtualSpeed.FromPower(200d):F1} km/h");
            await Assert.That(viewModel.SessionDistanceText).IsEqualTo("9 m");
            await Assert.That(viewModel.GetSessionSummary().DistanceMeters)
                .IsEqualTo(VirtualSpeed.FromPower(200d) / 3.6d).Within(1e-6);
        }
    }

    [Test]
    public async Task SessionDistance_DoesNotBridgeTelemetryGapsOrPausedTime()
    {
        using var platform = new TestDashboardPlatform();
        var viewModel = new TelemetryPresentationViewModel(platform);
        var start = DateTimeOffset.UnixEpoch;

        viewModel.Present(CreatePowerBikeSample(start, 0));
        viewModel.Present(CreatePowerBikeSample(start.AddSeconds(1), 1));
        viewModel.PauseSession(start.AddSeconds(1));
        viewModel.Present(CreatePowerBikeSample(start.AddSeconds(10), 1_000));
        viewModel.ResumeSession(start.AddSeconds(10));
        viewModel.Present(CreatePowerBikeSample(start.AddSeconds(11), 1_000));
        viewModel.Present(CreatePowerBikeSample(start.AddSeconds(30), 1_000));
        viewModel.Present(CreatePowerBikeSample(start.AddSeconds(31), 1_000));

        await Assert.That(viewModel.GetSessionSummary().DistanceMeters)
            .IsEqualTo(3d * VirtualSpeed.FromPower(200d) / 3.6d).Within(1e-6);
    }

    [Test]
    public async Task EffortMapSpeed_FollowsLiveSpeedAndStopsWhenPausedStaleOrCleared()
    {
        await using var environment = new DashboardTestEnvironment();
        await environment.ReadyAsync();
        await environment.ConnectAsync();
        var dashboard = environment.ViewModel;
        var snapshot = CreatePowerBikeSample(DateTimeOffset.UtcNow, 0);
        dashboard.Telemetry.Present(snapshot);
        await Assert.That(dashboard.EffortMapSpeedKilometersPerHour).IsEqualTo(0d);

        await dashboard.StartCommand.Execute().ToTask();
        dashboard.Telemetry.Present(snapshot);
        await Assert.That(dashboard.EffortMapSpeedKilometersPerHour).IsEqualTo(VirtualSpeed.FromPower(200d));

        dashboard.Telemetry.MarkStale();
        await Assert.That(dashboard.EffortMapSpeedKilometersPerHour).IsEqualTo(0d);
        dashboard.Telemetry.Present(snapshot);
        await Assert.That(dashboard.EffortMapSpeedKilometersPerHour).IsEqualTo(VirtualSpeed.FromPower(200d));

        dashboard.Telemetry.Present(VirtualSpeed.Apply(snapshot with { CadenceRpm = 0d }));
        await Assert.That(dashboard.EffortMapSpeedKilometersPerHour).IsEqualTo(0d);
        dashboard.Telemetry.Present(snapshot);
        await dashboard.StopCommand.Execute().ToTask();
        await Assert.That(dashboard.EffortMapSpeedKilometersPerHour).IsEqualTo(0d);

        await dashboard.StartCommand.Execute().ToTask();
        dashboard.Telemetry.Clear();
        await Assert.That(dashboard.EffortMapSpeedKilometersPerHour).IsEqualTo(0d);
    }

    [Test]
    public async Task HeartRateProfile_RestoresAndPersistsThroughPlatform()
    {
        using var platform = new TestDashboardPlatform();
        platform.SetPreference("HeartRateCyclingLactateThreshold", "160");
        using var viewModel = new HeartRatePresentationViewModel(platform);
        await Assert.That(viewModel.IsZoneProfileConfigured).IsTrue();

        viewModel.LactateThresholdText = "170";
        await Assert.That(viewModel.TryApplyCyclingLactateThreshold(out _)).IsTrue();
        await Assert.That(platform.GetPreference("HeartRateCyclingLactateThreshold", string.Empty)).IsEqualTo("170");

        viewModel.LactateThresholdText = "invalid";
        await Assert.That(viewModel.TryApplyCyclingLactateThreshold(out var error)).IsFalse();
        await Assert.That(string.IsNullOrEmpty(error)).IsFalse();
        await Assert.That(platform.GetPreference("HeartRateCyclingLactateThreshold", string.Empty)).IsEqualTo("170");
    }

    [Test]
    public async Task TargetCommands_PreserveParametersAvailabilityAndExecutionFeedback()
    {
        using var canApply = new BehaviorSubject<bool>(false);
        var response = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var appliedValue = 0d;
        using var viewModel = new TargetControlViewModel(
            "Power", "W", 0, 500, 5, FtmsMachineStatusOpcode.TargetPowerChanged,
            static value => value,
            (value, _) =>
            {
                appliedValue = value;
                return response.Task;
            }, canApply);

        await Assert.That(await viewModel.IncreaseCommand.CanExecute.FirstAsync()).IsFalse();
        canApply.OnNext(true);
        await Assert.That(await viewModel.IncreaseCommand.CanExecute.FirstAsync()).IsTrue();
        await viewModel.IncreaseCommand.Execute().ToTask();
        await Assert.That(viewModel.Value).IsEqualTo(5d);
        await viewModel.DecreaseCommand.Execute().ToTask();
        await Assert.That(viewModel.Value).IsEqualTo(0d);
        await viewModel.ApplyPresetCommand.Execute(150d).ToTask();

        var execution = viewModel.ApplyCommand.Execute(150d).ToTask();
        await Assert.That(viewModel.IsApplying).IsTrue();
        await Assert.That(appliedValue).IsEqualTo(150d);
        response.SetResult(true);
        await execution;
        await Assert.That(viewModel.IsApplying).IsFalse();
        await Assert.That(viewModel.ValueStateText.StartsWith("Accepted:", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task TargetCommand_ReportsFailuresThroughThrownExceptions()
    {
        var failure = new IOException("Target write failed.");
        using var viewModel = new TargetControlViewModel(
            "Power", "W", 0, 500, 5, FtmsMachineStatusOpcode.TargetPowerChanged,
            static value => value, (_, _) => Task.FromException<bool>(failure), Observable.Return(true));
        var exceptions = new List<Exception>();
        using var subscription = viewModel.ApplyCommand.ThrownExceptions.Subscribe(exceptions.Add);

        await Assert.That(async () => await viewModel.ApplyCommand.Execute(null).ToTask()).Throws<IOException>();
        await Assert.That(exceptions.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(exceptions[0], failure)).IsTrue();
        await Assert.That(viewModel.IsApplying).IsFalse();
    }

    [Test]
    public async Task AutoApply_RemainsDebouncedAndLatestOnly()
    {
        var scheduler = new HistoricalScheduler();
        var appliedValues = new List<double>();
        using var viewModel = new TargetControlViewModel(
            "Power", "W", 0, 500, 5, FtmsMachineStatusOpcode.TargetPowerChanged,
            static value => value,
            (value, _) =>
            {
                appliedValues.Add(value);
                return Task.FromResult(true);
            }, Observable.Return(true), autoApplyOnValueChange: true, scheduler);

        viewModel.Value = 100;
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100));
        viewModel.Value = 150;
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(249));
        await Assert.That(appliedValues.Count).IsEqualTo(0);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));
        await Assert.That(appliedValues.Count).IsEqualTo(1);
        await Assert.That(appliedValues[0]).IsEqualTo(150d);
    }

    [Test]
    public async Task TrainingCommands_LoadCatalogAndPreserveAvailabilityAndNavigation()
    {
        await using var environment = new DashboardTestEnvironment();
        await environment.ReadyAsync();
        var dashboard = environment.ViewModel;
        var training = dashboard.Training;
        var destinations = new List<DashboardDestination>();
        using var subscription = dashboard.NavigationRequests.Subscribe(destinations.Add);

        await Assert.That(training.Workouts.Count).IsEqualTo(35 + TrainingDebugCatalog.Workouts.Count);
        await Assert.That(training.Plans[^1].Id).IsEqualTo(TrainingDebugCatalog.PlanId);
        await Assert.That(await training.StartCommand.CanExecute.FirstAsync()).IsFalse();
        await Assert.That(await training.PauseCommand.CanExecute.FirstAsync()).IsFalse();
        await Assert.That(await training.SelectWorkoutCommand.CanExecute.FirstAsync()).IsTrue();
        await training.SelectWorkoutCommand.Execute(training.Workouts[1]).ToTask();
        await Assert.That(training.SelectedWorkout).IsEqualTo(training.Workouts[1]);
        await training.ShowAssessmentCommand.Execute().ToTask();
        await Assert.That(training.IsAssessmentView).IsTrue();
        await training.ShowWorkoutsCommand.Execute().ToTask();
        await Assert.That(training.IsWorkoutsView).IsTrue();
        await training.ShowHomeCommand.Execute().ToTask();
        await dashboard.ShowTrainingCommand.Execute().ToTask();
        await Assert.That(destinations.SequenceEqual([DashboardDestination.Today, DashboardDestination.Training])).IsTrue();
    }

    [Test]
    public async Task ScanCommand_UsesInjectedMainThreadDispatch()
    {
        await using var environment = new DashboardTestEnvironment();
        await environment.ReadyAsync();

        await environment.ViewModel.ScanCommand.Execute().ToTask();

        await Assert.That(environment.Platform.DispatchCount).IsEqualTo(1);
        await Assert.That(environment.ViewModel.IsScanning).IsFalse();
        await Assert.That(environment.ViewModel.DiscoveredDevices.Count).IsEqualTo(1);
        await Assert.That(environment.ViewModel.IsWorking).IsFalse();
    }

    [Test]
    public async Task ControlCommands_RemainSerializedAcrossGeneratedCommands()
    {
        await using var environment = new DashboardTestEnvironment();
        await environment.ReadyAsync();
        await environment.ConnectAsync();
        var response = new TaskCompletionSource<FtmsControlPointResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        environment.Session.ExecuteAsync = (opcode, _) => opcode == FtmsControlPointOpcode.RequestControl
            ? response.Task
            : Task.FromResult(FtmsControlPointResult.Success);

        var request = environment.ViewModel.RequestControlCommand.Execute().ToTask();
        var start = environment.ViewModel.StartCommand.Execute().ToTask();
        try
        {
            await Assert.That(environment.Session.Operations.Count).IsEqualTo(1);
            await Assert.That(environment.ViewModel.IsControlOperationExecuting).IsTrue();
        }
        finally
        {
            response.SetResult(FtmsControlPointResult.Success);
        }

        await Task.WhenAll(request, start);

        await Assert.That(environment.Session.Operations.SequenceEqual(
            [FtmsControlPointOpcode.RequestControl, FtmsControlPointOpcode.StartOrResume])).IsTrue();
        await Assert.That(environment.ViewModel.IsWorkoutActive).IsTrue();
        await Assert.That(environment.ViewModel.IsControlOperationExecuting).IsFalse();
    }

    [Test]
    public async Task ControlCommands_CancelWhileWaitingForSharedGate()
    {
        await using var environment = new DashboardTestEnvironment();
        await environment.ReadyAsync();
        await environment.ConnectAsync();
        var response = new TaskCompletionSource<FtmsControlPointResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        environment.Session.ExecuteAsync = (_, _) => response.Task;
        var request = environment.ViewModel.RequestControlCommand.Execute().ToTask();
        using var cancellation = new CancellationTokenSource();
        var start = environment.ViewModel.StartCommand.Execute().ToTask(cancellation.Token);

        try
        {
            cancellation.Cancel();
            await Assert.That(async () => await start).Throws<OperationCanceledException>();
        }
        finally
        {
            response.SetResult(FtmsControlPointResult.Success);
        }

        await request;
        await Assert.That(environment.Session.Operations.SequenceEqual([FtmsControlPointOpcode.RequestControl])).IsTrue();
        await Assert.That(environment.ViewModel.IsWorkoutActive).IsFalse();
    }

    [Test]
    public async Task ControlCommands_PreserveRejectedResponseState()
    {
        await using var environment = new DashboardTestEnvironment();
        await environment.ReadyAsync();
        await environment.ConnectAsync();
        environment.Session.ExecuteAsync = static (_, _) => Task.FromResult(FtmsControlPointResult.ControlNotPermitted);

        await environment.ViewModel.StartCommand.Execute().ToTask();

        await Assert.That(environment.ViewModel.IsWorkoutActive).IsFalse();
        await Assert.That(environment.ViewModel.HasControlPermission).IsFalse();
        await Assert.That(environment.ViewModel.ConnectionStatus.Contains("rejected", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ExportCommand_UsesInjectedCacheAndAppDataDirectories()
    {
        await using var environment = new DashboardTestEnvironment();
        await environment.ReadyAsync();
        await environment.ConnectAsync();

        await environment.ViewModel.ExportSessionCommand.Execute().ToTask();

        var exports = Directory.GetFiles(environment.Platform.CacheDirectory, "stationary-session-*.csv");
        await Assert.That(exports.Length).IsEqualTo(1);
        var archive = await RecordedSessionArchiveStorage.ReadJsonAsync(
            Path.Combine(environment.Platform.AppDataDirectory, "previous-session.json"));
        await Assert.That(archive is not null).IsTrue();
        await Assert.That(environment.ViewModel.ConnectionStatus.Contains(exports[0], StringComparison.Ordinal)).IsTrue();
    }

    private static TelemetrySnapshot CreatePowerBikeSample(DateTimeOffset capturedAt, uint totalDistanceMeters) =>
        VirtualSpeed.Apply(new(
            capturedAt,
            FtmsMachineDataType.IndoorBike,
            null,
            80d,
            200,
            null,
            null,
            totalDistanceMeters,
            ReportedFields: TelemetryField.Power | TelemetryField.TotalDistance));
}