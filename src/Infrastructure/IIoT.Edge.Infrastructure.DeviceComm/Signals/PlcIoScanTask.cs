using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Runtime.Base;

namespace IIoT.Edge.Infrastructure.DeviceComm.Signals;

/// <summary>
/// 基于具体 PLC 通讯服务的 IO 扫描任务，状态上报留在基础设施层，扫描循环由 Runtime 基类承载。
/// </summary>
public sealed class PlcIoScanTask : PlcIoScanTaskBase
{
    private readonly PlcConnectionStatusStore? _statusStore;

    public PlcIoScanTask(
        IPlcService plcService,
        IPlcDataStore dataStore,
        NetworkDeviceEntity deviceConfig,
        IReadOnlyCollection<IoMappingEntity> ioMappings,
        ILogService logger,
        PlcConnectionStatusStore? statusStore = null)
        : base(
            plcService,
            dataStore,
            new PlcIoScanDevice(
                deviceConfig.Id,
                deviceConfig.DeviceName,
                deviceConfig.IpAddress,
                deviceConfig.Port1),
            ioMappings.Select(static mapping => new PlcIoScanMapping(
                mapping.PlcAddress,
                mapping.AddressCount,
                mapping.Direction,
                mapping.SortOrder)),
            logger)
    {
        _statusStore = statusStore;
    }

    protected override void MarkConnected()
        => _statusStore?.MarkConnected(DeviceId, DeviceName);

    protected override void MarkDisconnected(string reason)
        => _statusStore?.MarkDisconnected(DeviceId, DeviceName, reason);
}
