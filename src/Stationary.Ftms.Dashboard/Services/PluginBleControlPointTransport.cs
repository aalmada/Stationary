using Plugin.BLE.Abstractions.Contracts;

using Stationary.Ftms.Client;

namespace Stationary.Ftms.Dashboard.Services;

public sealed class PluginBleControlPointTransport(ICharacteristic characteristic) : IFtmsControlPointTransport
{
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await characteristic.WriteAsync(payload.ToArray(), cancellationToken).ConfigureAwait(false);
    }
}