// 路径：src/Infrastructure/IIoT.Edge.PlcDevice/Store/PlcBuffer.cs
using IIoT.Edge.Contracts.Plc.Store;

namespace IIoT.Edge.PlcDevice.Store;

public class PlcBuffer : IPlcBufferTransport
{
    private readonly ushort[] _readBuffer;
    private readonly ushort[] _writeBuffer;

    public PlcBuffer(int readSize, int writeSize)
    {
        _readBuffer = new ushort[readSize];
        _writeBuffer = new ushort[writeSize];
    }

    // ── IPlcBuffer（任务用）──

    public ushort GetReadValue(int index)
        => index >= 0 && index < _readBuffer.Length ? _readBuffer[index] : (ushort)0;

    public void SetWriteValue(int index, ushort value)
    {
        if (index >= 0 && index < _writeBuffer.Length)
            _writeBuffer[index] = value;
    }

    // ── IPlcBufferTransport（SignalInteraction用）──

    public void UpdateReadBuffer(ushort[] data)
        => Array.Copy(data, _readBuffer, Math.Min(data.Length, _readBuffer.Length));

    public ushort[] GetWriteBuffer()
        => _writeBuffer;
}