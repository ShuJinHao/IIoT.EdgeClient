using IIoT.Edge.Application.Abstractions.Plc.Store;
using System.Threading;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;

public class PlcBuffer : IPlcBufferTransport
{
    private ushort[] _readBuffer;
    private readonly ushort[] _writeBuffer;
    private readonly object _writeSync = new();
    private ushort[] _writeSnapshot;
    private bool _writeSnapshotDirty = true;

    public PlcBuffer(int readSize, int writeSize)
    {
        _readBuffer = new ushort[readSize];
        _writeBuffer = new ushort[writeSize];
        _writeSnapshot = new ushort[writeSize];
    }

    public ushort GetReadValue(int index)
    {
        var snapshot = Volatile.Read(ref _readBuffer);
        return index >= 0 && index < snapshot.Length ? snapshot[index] : (ushort)0;
    }

    public void SetWriteValue(int index, ushort value)
    {
        lock (_writeSync)
        {
            if (index < 0 || index >= _writeBuffer.Length)
            {
                return;
            }

            _writeBuffer[index] = value;
            _writeSnapshotDirty = true;
        }
    }

    public void UpdateReadBuffer(ushort[] data)
    {
        var next = new ushort[_readBuffer.Length];
        Array.Copy(data, next, Math.Min(data.Length, next.Length));
        Interlocked.Exchange(ref _readBuffer, next);
    }

    public ushort[] GetWriteBuffer()
    {
        lock (_writeSync)
        {
            if (_writeSnapshotDirty || _writeSnapshot.Length != _writeBuffer.Length)
            {
                _writeSnapshot = (ushort[])_writeBuffer.Clone();
                _writeSnapshotDirty = false;
            }

            return _writeSnapshot;
        }
    }

    public bool Matches(int readSize, int writeSize)
    {
        var readLength = Volatile.Read(ref _readBuffer).Length;
        return readLength == readSize && _writeBuffer.Length == writeSize;
    }
}
