namespace IIoT.Edge.Application.Abstractions.Plc.Store;

/// <summary>
/// PLC 数据缓冲存储契约，按网络设备维护运行态信号快照。
/// </summary>
public interface IPlcDataStore
{
    void Register(int networkDeviceId, int readSize, int writeSize);

    void Register(
        int networkDeviceId,
        int readSize,
        int writeSize,
        IReadOnlyCollection<PlcBufferSignalBinding> signalBindings);

    /// <summary>
    /// 获取运行期信号交互使用的传输缓冲区。
    /// </summary>
    IPlcBufferTransport? GetBuffer(int networkDeviceId);

    bool HasDevice(int networkDeviceId);
}
