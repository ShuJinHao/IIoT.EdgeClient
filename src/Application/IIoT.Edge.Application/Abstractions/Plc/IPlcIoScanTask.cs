namespace IIoT.Edge.Application.Abstractions.Plc;

/// <summary>
/// PLC IO 扫描任务契约，负责把现场 PLC 与本地缓冲区做周期性读写搬运。
/// </summary>
public interface IPlcIoScanTask : IPlcTask
{
    /// <summary>
    /// 当前 PLC 连接是否可用。
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 建立或恢复 PLC 连接。
    /// </summary>
    Task ConnectAsync();
}
