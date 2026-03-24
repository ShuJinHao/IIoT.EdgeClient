// 路径：src/Shared/IIoT.Edge.Contracts/Plc/Store/IPlcDataStore.cs
namespace IIoT.Edge.Contracts.Plc.Store;

public interface IPlcDataStore
{
    void Register(int networkDeviceId, int readSize, int writeSize);

    /// <summary>
    /// 返回 IPlcBufferTransport，SignalInteraction直接用
    /// 传给任务时自动向上转为 IPlcBuffer
    /// </summary>
    IPlcBufferTransport? GetBuffer(int networkDeviceId);

    bool HasDevice(int networkDeviceId);
}