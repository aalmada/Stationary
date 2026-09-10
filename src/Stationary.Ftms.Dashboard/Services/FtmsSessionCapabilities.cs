using Stationary.Ftms;

namespace Stationary.Ftms.Dashboard.Services;

public readonly record struct FtmsSupportedRanges(
    FtmsUInt16Range? Speed,
    FtmsInt16Range? Inclination,
    FtmsInt16Range? Resistance,
    FtmsUInt16Range? Power,
    FtmsByteRange? HeartRate);

public sealed class FtmsMachineStatusChangedEventArgs(
    FtmsMachineStatusOpcode opcode,
    ReadOnlyMemory<byte> parameters) : EventArgs
{
    public FtmsMachineStatusOpcode Opcode { get; } = opcode;

    public ReadOnlyMemory<byte> Parameters { get; } = parameters;
}