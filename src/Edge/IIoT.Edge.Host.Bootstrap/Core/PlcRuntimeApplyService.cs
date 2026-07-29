using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Shell.Core;

public sealed class PlcRuntimeApplyService(
    IReadRepository<NetworkDeviceEntity> networkDevices,
    IPlcRuntimeTaskBinder runtimeTaskBinder,
    IPlcConnectionManager plcConnectionManager,
    ILogService logger) : IPlcRuntimeApplyService
{
    public async Task ApplyDeviceRuntimeAsync(
        int networkDeviceId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException("网络设备 Id 必须大于 0。", nameof(networkDeviceId));
        }

        var device = await networkDevices
            .GetByIdAsync(networkDeviceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("未找到要应用运行配置的 PLC 设备。");

        await ApplyDeviceRuntimeCoreAsync(device, reason, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ApplyDeviceRuntimeAsync(
        string deviceName,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);

        var normalizedName = deviceName.Trim();
        var device = (await networkDevices
                .GetListAsync(x => x.DeviceName == normalizedName, cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault();
        if (device is null)
        {
            throw new InvalidOperationException($"未找到要应用运行配置的 PLC 设备“{normalizedName}”。");
        }

        await ApplyDeviceRuntimeCoreAsync(device, reason, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ApplyDeviceRuntimeCoreAsync(
        NetworkDeviceEntity device,
        string reason,
        CancellationToken cancellationToken)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "运行配置变更" : reason.Trim();
        if (string.Equals(
                normalizedReason,
                PlcRuntimeApplyReasons.TaskBindingSave,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "PLC 任务绑定禁止通过独立运行时应用入口执行；必须使用 SQLite 与 TaskKey 增量一体化事务命令。");
        }

        await runtimeTaskBinder
            .BindDeviceAsync(
                device.Id,
                applyToRunningDevice: false,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await plcConnectionManager
            .ReloadAsync(device.DeviceName, cancellationToken)
            .ConfigureAwait(false);
        logger.Info($"[{device.DeviceName}] PLC 运行配置已通过整机重载应用：{normalizedReason}。");
    }
}
