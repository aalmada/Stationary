namespace Stationary.Ftms.Client;

public interface IFtmsControlPointTransport
{
    ValueTask WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}