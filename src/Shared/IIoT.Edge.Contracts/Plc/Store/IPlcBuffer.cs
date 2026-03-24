// 路径：src/Shared/IIoT.Edge.Contracts/Plc/Store/IPlcBuffer.cs
namespace IIoT.Edge.Contracts.Plc.Store;

/// <summary>
/// PLC缓冲区 — 任务侧接口（只能按索引读写信号）
/// </summary>
public interface IPlcBuffer
{
    ushort GetReadValue(int index);
    void SetWriteValue(int index, ushort value);
}

/// <summary>
/// PLC缓冲区 — 搬运侧接口（SignalInteraction用，批量更新和获取整块数据）
/// 继承 IPlcBuffer，所以 SignalInteraction 拿到这个接口也能按索引操作
/// </summary>
public interface IPlcBufferTransport : IPlcBuffer
{
    void UpdateReadBuffer(ushort[] data);
    ushort[] GetWriteBuffer();
}