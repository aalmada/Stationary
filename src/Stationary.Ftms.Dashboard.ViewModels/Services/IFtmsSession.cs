using Stationary.Ftms;
using Stationary.Ftms.Dashboard.Core;

namespace Stationary.Ftms.Dashboard.Services;

public interface IFtmsSession : IAsyncDisposable
{
    string DeviceName { get; }

    FtmsMachineDataType MachineType { get; }

    IReadOnlyList<FtmsMachineDataType> AdvertisedMachineTypes { get; }

    long TelemetryPacketsReceived { get; }

    FitnessMachineFeature? Features { get; }

    FtmsSupportedRanges SupportedRanges { get; }

    bool CanControl { get; }

    string ControlPointAvailability => CanControl
        ? "FTMS Control Point ready."
        : "The FTMS Control Point is unavailable.";

    bool IsMachineStatusSubscribed => false;

    string MachineStatusAvailability => "Fitness Machine Status is unavailable.";

    long MachineStatusPacketsReceived => 0;

    FtmsMachineStatusChangedEventArgs? LatestMachineStatus => null;

    LatestTelemetryBuffer Telemetry { get; }

    event EventHandler? TelemetryReceived;

    event EventHandler? ConnectionLost;

    event EventHandler<FtmsMachineStatusChangedEventArgs>? MachineStatusChanged;

    ValueTask<FtmsControlPointResult> ExecuteControlAsync(FtmsControlPointOpcode opcode, ReadOnlyMemory<byte> parameters, CancellationToken cancellationToken);
}