namespace IIoT.Edge.Application.Abstractions.Device;

/// <summary>
/// 设备网络状态。
///
/// 由 DeviceService 的心跳循环维护。
/// 心跳通过调用云端寻址接口判断当前网络和设备会话是否可用。
/// </summary>
public enum NetworkState
{
    /// <summary>
    /// 网络不可用，数据写入本地缓冲，例如 Excel 和 SQLite 重传队列。
    /// </summary>
    Offline,

    /// <summary>
    /// 网络可用，优先执行实时上报，并处理可补传的积压数据。
    /// </summary>
    Online
}
