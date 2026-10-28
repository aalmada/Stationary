namespace Stationary.Ftms.Dashboard.Services;

public interface IFtmsDiscoveryService
{
    ValueTask DiscoverAsync(Action<FtmsDiscoveredDevice> deviceDiscovered, CancellationToken cancellationToken);

    ValueTask<IFtmsSession?> ConnectAsync(FtmsDiscoveredDevice device, CancellationToken cancellationToken);
}