using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Domain.Hardware.Aggregates;
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

        await ApplyDeviceRuntimeCoreAsync(device.DeviceName, reason, cancellationToken)
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

        await ApplyDeviceRuntimeCoreAsync(device.DeviceName, reason, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ApplyDeviceRuntimeCoreAsync(
        string deviceName,
        string reason,
        CancellationToken cancellationToken)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "运行配置变更" : reason.Trim();

        await runtimeTaskBinder.BindAsync(cancellationToken).ConfigureAwait(false);
        await plcConnectionManager.ReloadAsync(deviceName, cancellationToken).ConfigureAwait(false);
        logger.Info($"[{deviceName}] PLC 运行配置已应用：{normalizedReason}。");
    }
}
