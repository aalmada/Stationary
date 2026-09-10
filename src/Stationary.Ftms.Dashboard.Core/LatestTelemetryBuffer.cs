using System.Threading.Channels;

namespace Stationary.Ftms.Dashboard.Core;

public sealed class LatestTelemetryBuffer
{
    private readonly Channel<TelemetrySnapshot> channel = Channel.CreateBounded<TelemetrySnapshot>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public ChannelReader<TelemetrySnapshot> Reader => channel.Reader;

    public bool TryPublish(TelemetrySnapshot snapshot) => channel.Writer.TryWrite(snapshot);

    public void Complete(Exception? error = null) => channel.Writer.TryComplete(error);
}