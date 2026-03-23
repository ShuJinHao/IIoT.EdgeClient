namespace IIoT.Edge.Contracts.Plc.Store;

public interface IPlcBuffer
{
    void UpdateReadBuffer(ushort[] data);
    ushort GetReadValue(int index);
    void SetWriteValue(int index, ushort value);
    ushort[] GetWriteBuffer();
}