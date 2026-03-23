namespace IIoT.Edge.Contracts.Plc.Store;

public interface IPlcDataStore
{
    void Register(int networkDeviceId, int readSize, int writeSize);
    IPlcBuffer? GetBuffer(int networkDeviceId);
    bool HasDevice(int networkDeviceId);
}