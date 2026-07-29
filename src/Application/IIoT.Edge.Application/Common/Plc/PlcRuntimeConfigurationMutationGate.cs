using System.Collections.Concurrent;

namespace IIoT.Edge.Application.Common.Plc;

public interface IPlcRuntimeConfigurationMutationGate
{
    ValueTask<IDisposable> EnterAsync(
        int networkDeviceId,
        CancellationToken cancellationToken = default);
}

public sealed class PlcRuntimeConfigurationMutationGate
    : IPlcRuntimeConfigurationMutationGate
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _deviceGates = new();

    public async ValueTask<IDisposable> EnterAsync(
        int networkDeviceId,
        CancellationToken cancellationToken = default)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException(
                "PLC 运行配置变更门必须绑定有效的 NetworkDeviceId。",
                nameof(networkDeviceId));
        }

        var gate = _deviceGates.GetOrAdd(
            networkDeviceId,
            static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new MutationLease(gate);
    }

    private sealed class MutationLease(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
