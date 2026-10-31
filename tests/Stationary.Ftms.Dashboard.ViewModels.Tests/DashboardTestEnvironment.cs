using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using ReactiveUI;

using Stationary.Ftms.Dashboard.Core;
using Stationary.Ftms.Dashboard.Services;

namespace Stationary.Ftms.Dashboard.ViewModels.Tests;

internal sealed class DashboardTestEnvironment : IAsyncDisposable
{
    public DashboardTestEnvironment(TestFtmsSession? session = null)
    {
        Platform = new TestDashboardPlatform();
        Session = session ?? new TestFtmsSession();
        HeartRateDiscovery = new TestHeartRateDiscoveryService();
        ViewModel = new(new TestFtmsDiscoveryService(Session), HeartRateDiscovery, Platform, new HistoricalScheduler());
    }

    public TestDashboardPlatform Platform { get; }

    public TestFtmsSession Session { get; }

    public TestHeartRateDiscoveryService HeartRateDiscovery { get; }

    public DashboardViewModel ViewModel { get; }

    public async Task ReadyAsync() =>
        await ViewModel.Training.WhenAnyValue(viewModel => viewModel.IsCatalogLoading)
            .Where(static loading => !loading)
            .FirstAsync()
            .ToTask()
            .WaitAsync(TimeSpan.FromSeconds(10));

    public async Task ConnectAsync()
    {
        ViewModel.SelectedDevice = new("test-device", "Test bike", "Indoor bike");
        await ViewModel.ConnectCommand.Execute().ToTask();
    }

    public async ValueTask DisposeAsync()
    {
        await ViewModel.DisposeAsync();
        Platform.Dispose();
    }
}

internal sealed class TestDashboardPlatform : IDashboardPlatform, IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"stationary-viewmodels-{Guid.NewGuid():N}");
    private readonly Dictionary<string, bool> booleanPreferences = [];
    private readonly Dictionary<string, string> stringPreferences = [];

    public TestDashboardPlatform()
    {
        CacheDirectory = Directory.CreateDirectory(Path.Combine(root, "cache")).FullName;
        AppDataDirectory = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
    }

    public string CacheDirectory { get; }

    public string AppDataDirectory { get; }

    public int DispatchCount { get; private set; }

    public Exception? PreferenceWriteException { get; set; }

    public bool GetPreference(string key, bool defaultValue) => booleanPreferences.GetValueOrDefault(key, defaultValue);

    public string GetPreference(string key, string defaultValue) => stringPreferences.GetValueOrDefault(key, defaultValue);

    public void SetPreference(string key, bool value)
    {
        if (PreferenceWriteException is { } exception)
        {
            throw exception;
        }

        booleanPreferences[key] = value;
    }

    public void SetPreference(string key, string value) => stringPreferences[key] = value;

    public void RemovePreference(string key)
    {
        booleanPreferences.Remove(key);
        stringPreferences.Remove(key);
    }

    public Task InvokeOnMainThreadAsync(Action action)
    {
        DispatchCount++;
        action();
        return Task.CompletedTask;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}

internal sealed class TestFtmsDiscoveryService(TestFtmsSession session) : IFtmsDiscoveryService
{
    public ValueTask DiscoverAsync(Action<FtmsDiscoveredDevice> deviceDiscovered, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        deviceDiscovered(new("test-device", "Test bike", "Indoor bike"));
        return ValueTask.CompletedTask;
    }

    public ValueTask<IFtmsSession?> ConnectAsync(FtmsDiscoveredDevice device, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IFtmsSession?>(session);
}

internal sealed class TestHeartRateDiscoveryService : IHeartRateDiscoveryService
{
    public Func<CancellationToken, ValueTask<IHeartRateSession?>>? Connect { get; set; }

    public ValueTask DiscoverAsync(Action<HeartRateDiscoveredDevice> deviceDiscovered, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask<IHeartRateSession?> ConnectAsync(HeartRateDiscoveredDevice device, CancellationToken cancellationToken) =>
        Connect is { } connect ? connect(cancellationToken) : ValueTask.FromResult<IHeartRateSession?>(null);
}

internal sealed class TestFtmsSession : IFtmsSession
{
    public string DeviceName => "Test bike";

    public FtmsMachineDataType MachineType => FtmsMachineDataType.IndoorBike;

    public IReadOnlyList<FtmsMachineDataType> AdvertisedMachineTypes => [MachineType];

    public long TelemetryPacketsReceived => 0;

    public FitnessMachineFeature? Features { get; init; }

    public FtmsSupportedRanges SupportedRanges { get; init; }

    public bool CanControl => true;

    public LatestTelemetryBuffer Telemetry { get; } = new();

    public List<FtmsControlPointOpcode> Operations { get; } = [];

    public Func<FtmsControlPointOpcode, CancellationToken, Task<FtmsControlPointResult>>? ExecuteAsync { get; set; }

    public event EventHandler? TelemetryReceived
    {
        add { }
        remove { }
    }

    public event EventHandler? ConnectionLost
    {
        add { }
        remove { }
    }

    public event EventHandler<FtmsMachineStatusChangedEventArgs>? MachineStatusChanged
    {
        add { }
        remove { }
    }

    public async ValueTask<FtmsControlPointResult> ExecuteControlAsync(FtmsControlPointOpcode opcode, ReadOnlyMemory<byte> parameters, CancellationToken cancellationToken)
    {
        Operations.Add(opcode);
        return ExecuteAsync is { } execute
            ? await execute(opcode, cancellationToken)
            : FtmsControlPointResult.Success;
    }

    public ValueTask DisposeAsync()
    {
        Telemetry.Complete();
        return ValueTask.CompletedTask;
    }
}