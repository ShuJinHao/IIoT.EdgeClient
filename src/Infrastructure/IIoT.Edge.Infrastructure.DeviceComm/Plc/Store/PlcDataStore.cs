using IIoT.Edge.Application.Abstractions.Plc.Store;
using System.Collections.Concurrent;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;

public class PlcDataStore : IPlcDataStore
{
    private readonly ConcurrentDictionary<int, PlcBuffer> _buffers = new();

    public void Register(int networkDeviceId, int readSize, int writeSize)
        => Register(networkDeviceId, readSize, writeSize, []);

    public void Register(
        int networkDeviceId,
        int readSize,
        int writeSize,
        IReadOnlyCollection<PlcBufferSignalBinding> signalBindings)
    {
        _buffers.AddOrUpdate(
            networkDeviceId,
            _ => new PlcBuffer(readSize, writeSize, signalBindings),
            (_, existing) =>
            {
                if (!existing.Matches(readSize, writeSize))
                {
                    return new PlcBuffer(readSize, writeSize, signalBindings);
                }

                existing.SetSignalBindings(signalBindings);
                return existing;
            });
    }

    public IPlcBufferTransport? GetBuffer(int networkDeviceId)
        => _buffers.TryGetValue(networkDeviceId, out var buffer) ? buffer : null;

    public bool HasDevice(int networkDeviceId)
        => _buffers.ContainsKey(networkDeviceId);
}
