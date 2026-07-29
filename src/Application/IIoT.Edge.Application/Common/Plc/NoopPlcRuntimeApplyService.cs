using IIoT.Edge.Module.Contracts.Plc;

namespace IIoT.Edge.Application.Common.Plc;

/// <summary>
/// 非宿主环境下的空实现，真实 Shell 由 Host.Bootstrap 覆盖为运行时实现。
/// </summary>
public sealed class NoopPlcRuntimeApplyService : IPlcRuntimeApplyService
{
    public Task ApplyDeviceRuntimeAsync(
        int networkDeviceId,
        string reason,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ApplyDeviceRuntimeAsync(
        string deviceName,
        string reason,
        CancellationToken cancellationToken = default)
        => Task.FromException(
            new NotSupportedException(
                "按 DeviceName 应用 PLC 运行配置的入口已停用；必须使用稳定 NetworkDeviceId。"));
}
