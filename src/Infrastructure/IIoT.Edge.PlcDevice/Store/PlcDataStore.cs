using IIoT.Edge.Contracts.Plc.Store;

namespace IIoT.Edge.PlcDevice.Store;

public class PlcDataStore : IPlcDataStore
{
    private readonly Dictionary<int, PlcBuffer> _buffers = new();

    public void Register(int networkDeviceId, int readSize, int writeSize)
    {
        if (!_buffers.ContainsKey(networkDeviceId))
            _buffers[networkDeviceId] = new PlcBuffer(readSize, writeSize);
    }

    public IPlcBuffer? GetBuffer(int networkDeviceId)
        => _buffers.TryGetValue(networkDeviceId, out var buffer) ? buffer : null;

    public bool HasDevice(int networkDeviceId)
        => _buffers.ContainsKey(networkDeviceId);
}