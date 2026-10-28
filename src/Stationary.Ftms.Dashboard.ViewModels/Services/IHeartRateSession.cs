using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Services;

public interface IHeartRateSession : IAsyncDisposable
{
    string DeviceName { get; }

    long MeasurementsReceived { get; }

    LatestHeartRateBuffer Telemetry { get; }

    event EventHandler? MeasurementReceived;

    event EventHandler? ConnectionLost;
}