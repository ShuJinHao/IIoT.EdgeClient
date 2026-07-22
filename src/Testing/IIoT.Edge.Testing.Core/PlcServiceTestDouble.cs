using IIoT.Edge.Module.Contracts.Plc;

namespace IIoT.Edge.Testing;

public abstract class PlcServiceTestDouble : IPlcService
{
    public virtual bool IsConnected { get; protected set; }

    public virtual void Init(PlcEndpoint endpoint)
    {
    }

    public virtual Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public virtual Task DisconnectAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public virtual Task<List<T>> ReadDataAsync<T>(
        string address,
        ushort length,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public virtual Task WriteDataAsync<T>(
        string address,
        List<T> data,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public virtual ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}
