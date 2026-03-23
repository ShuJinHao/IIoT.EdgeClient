using IIoT.Edge.Contracts.Plc.Store;

namespace IIoT.Edge.PlcDevice.Store;

public class PlcBuffer: IPlcBuffer
{
    public ushort[] ReadBuffer { get; private set; }
    public ushort[] WriteBuffer { get; private set; }

    public PlcBuffer(int readSize, int writeSize)
    {
        ReadBuffer = new ushort[readSize];
        WriteBuffer = new ushort[writeSize];
    }

    public void UpdateReadBuffer(ushort[] data)
        => Array.Copy(data, ReadBuffer, Math.Min(data.Length, ReadBuffer.Length));

    public ushort GetReadValue(int index)
        => index >= 0 && index < ReadBuffer.Length ? ReadBuffer[index] : (ushort)0;

    public void SetWriteValue(int index, ushort value)
    {
        if (index >= 0 && index < WriteBuffer.Length)
            WriteBuffer[index] = value;
    }

    public ushort[] GetWriteBuffer()
        => WriteBuffer;
}