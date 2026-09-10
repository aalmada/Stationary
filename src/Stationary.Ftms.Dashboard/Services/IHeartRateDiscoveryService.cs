namespace Stationary.Ftms.Dashboard.Services;

public interface IHeartRateDiscoveryService
{
    ValueTask DiscoverAsync(Action<HeartRateDiscoveredDevice> deviceDiscovered, CancellationToken cancellationToken);

    ValueTask<IHeartRateSession?> ConnectAsync(HeartRateDiscoveredDevice device, CancellationToken cancellationToken);
}