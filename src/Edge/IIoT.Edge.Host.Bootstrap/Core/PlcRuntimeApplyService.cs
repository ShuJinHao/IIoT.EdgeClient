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
            var result = await runtimeTaskBinder
                .BindDeviceAsync(
                    device.Id,
                    applyToRunningDevice: true,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var stateText = result.State switch
            {
                PlcRuntimeTaskApplyState.Applied => "已按 TaskKey 增量应用",
                PlcRuntimeTaskApplyState.WaitingForConnection => "已保存，等待 PLC 连接后应用",
                PlcRuntimeTaskApplyState.WaitingForRuntime => "已保存，等待 PLC runtime 启动后应用",
                _ => throw new ArgumentOutOfRangeException(nameof(result.State))
            };
            logger.Info(
                $"[{device.DeviceName}] PLC 任务绑定{stateText}：{string.Join("、", result.EnabledTaskKeys)}。");
            return;
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
