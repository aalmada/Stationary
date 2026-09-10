namespace Stationary.Ftms.Client;

public sealed class FtmsControlPointCoordinator(IFtmsControlPointTransport transport) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock sync = new();
    private TaskCompletionSource<FtmsControlPointResult>? pendingResponse;
    private FtmsControlPointOpcode pendingOpcode;
    private int disposed;

    public async ValueTask<FtmsControlPointResult> ExecuteAsync(
        FtmsControlPointOpcode opcode,
        ReadOnlyMemory<byte> parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        TaskCompletionSource<FtmsControlPointResult>? response = null;
        try
        {
            var payload = new byte[parameters.Length + 1];
            if (!FitnessMachineControlPoint.TryEncode(opcode, parameters.Span, payload, out _))
            {
                throw new ArgumentException("The parameters do not match the FTMS control-point opcode.", nameof(parameters));
            }

            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed != 0, this);
                pendingOpcode = opcode;
                pendingResponse = response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            await transport.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            return await response.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(pendingResponse, response))
                {
                    pendingResponse = null;
                    pendingOpcode = default;
                }
            }

            gate.Release();
        }
    }

    public bool HandleIndication(ReadOnlySpan<byte> payload)
    {
        lock (sync)
        {
            if (payload.Length < 3 ||
                payload[0] != (byte)FtmsControlPointOpcode.ResponseCode ||
                payload[1] != (byte)pendingOpcode ||
                !Enum.IsDefined((FtmsControlPointResult)payload[2]))
            {
                return false;
            }

            return pendingResponse?.TrySetResult((FtmsControlPointResult)payload[2]) == true;
        }
    }

    public void Dispose()
    {
        TaskCompletionSource<FtmsControlPointResult>? response;
        lock (sync)
        {
            if (disposed != 0)
            {
                return;
            }

            disposed = 1;
            response = pendingResponse;
        }

        response?.TrySetCanceled();
    }
}