using System.Threading.Channels;

namespace Stationary.Ftms.Dashboard.Core;

public sealed class LatestHeartRateBuffer
{
    private readonly Channel<HeartRateObservation> channel = Channel.CreateBounded<HeartRateObservation>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public ChannelReader<HeartRateObservation> Reader => channel.Reader;

    public bool TryPublish(HeartRateObservation observation) => channel.Writer.TryWrite(observation);

    public void Complete(Exception? error = null) => channel.Writer.TryComplete(error);
}