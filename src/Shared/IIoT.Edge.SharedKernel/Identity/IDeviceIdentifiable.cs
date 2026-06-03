namespace IIoT.Edge.SharedKernel.Identity;

/// <summary>
/// 统一设备身份标识接口，用于跨上下文、运行快照和设备配置按设备 ID 或名称匹配。
/// </summary>
public interface IDeviceIdentifiable
{
    /// <summary>
    /// 网络设备配置 ID；未绑定配置时为 0。
    /// </summary>
    int NetworkDeviceId { get; }

    /// <summary>
    /// 现场设备名称，用于运行态和配置态的兜底匹配。
    /// </summary>
    string DeviceName { get; }
}
