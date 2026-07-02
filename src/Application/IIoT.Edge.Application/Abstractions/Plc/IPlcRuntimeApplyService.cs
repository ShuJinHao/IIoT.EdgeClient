namespace IIoT.Edge.Application.Abstractions.Plc;

/// <summary>
/// 将已保存的 PLC 运行相关配置应用到当前运行时。
/// </summary>
public interface IPlcRuntimeApplyService
{
    Task ApplyDeviceRuntimeAsync(
        int networkDeviceId,
        string reason,
        CancellationToken cancellationToken = default);

    Task ApplyDeviceRuntimeAsync(
        string deviceName,
        string reason,
        CancellationToken cancellationToken = default);
}
