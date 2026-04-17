using IIoT.Edge.Application.Abstractions.Plc.Store;
using System.Collections.Concurrent;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;

public class PlcDataStore : IPlcDataStore
{
    private readonly ConcurrentDictionary<int, PlcBuffer> _buffers = new();

    public void Register(int networkDeviceId, int readSize, int writeSize)
    {
        _buffers.AddOrUpdate(
            networkDeviceId,
            _ => new PlcBuffer(readSize, writeSize),
            (_, existing) => existing.Matches(readSize, writeSize)
                ? existing
                : new PlcBuffer(readSize, writeSize));
    }

    public IPlcBufferTransport? GetBuffer(int networkDeviceId)
        => _buffers.TryGetValue(networkDeviceId, out var buffer) ? buffer : null;

    public bool HasDevice(int networkDeviceId)
        => _buffers.ContainsKey(networkDeviceId);
}
