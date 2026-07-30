using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Signals;

/// <summary>
/// 基于具体 PLC 通讯服务的 IO 扫描任务，状态上报留在基础设施层。
/// </summary>
public sealed class PlcIoScanTask : PlcIoScanTaskBase
{
    private readonly PlcConnectionStatusStore? _statusStore;
    private readonly Action<bool>? _connectionStateChanged;
    private readonly string _plcCode;

    public PlcIoScanTask(
        IPlcService plcService,
        IPlcDataStore dataStore,
        NetworkDeviceEntity deviceConfig,
        IReadOnlyCollection<IoMappingEntity> ioMappings,
        ILogService logger,
        IPlcSignalBlockPlanner signalBlockPlanner,
        PlcConnectionStatusStore? statusStore = null,
        PlcIoRuntimePolicy? runtimePolicy = null,
        PlcEndpoint? endpoint = null,
        Action<bool>? connectionStateChanged = null)
        : base(
            plcService,
            dataStore,
            new PlcIoScanDevice(
                deviceConfig.Id,
                deviceConfig.DeviceName,
                endpoint ?? new TcpPlcEndpoint(
                    deviceConfig.IpAddress,
                    deviceConfig.Port1,
                    deviceConfig.ConnectTimeout))
            {
                PlcCode = deviceConfig.PlcCode
            },
            ioMappings
                .Where(static mapping => !string.IsNullOrWhiteSpace(mapping.PlcAddress))
                .Select(static mapping => new PlcIoScanMapping(
                mapping.SignalKey,
                mapping.PlcAddress,
                mapping.AddressCount,
                mapping.DataType,
                mapping.Direction,
                mapping.Category,
                mapping.SortOrder)),
            logger,
            signalBlockPlanner,
            runtimePolicy)
    {
        _statusStore = statusStore;
        _connectionStateChanged = connectionStateChanged;
        _plcCode = deviceConfig.PlcCode;
    }

    protected override void MarkConnected(int? latencyMs)
    {
        _statusStore?.MarkConnected(DeviceId, _plcCode, DeviceName, latencyMs);
        _connectionStateChanged?.Invoke(true);
    }

    protected override bool MarkProtocolSuccess(int? latencyMs)
        => _statusStore?.MarkProtocolSuccess(DeviceId, _plcCode, DeviceName, latencyMs) ?? true;

    protected override bool IsStableOnline()
        => _statusStore?.IsStableOnline(DeviceId) ?? true;

    protected override void MarkRuntimeFault(string reason)
    {
        _statusStore?.MarkRuntimeFault(DeviceId, _plcCode, DeviceName, reason);
        _connectionStateChanged?.Invoke(false);
    }

    protected override void MarkConnecting()
    {
        _statusStore?.MarkConnecting(DeviceId, _plcCode, DeviceName);
        _connectionStateChanged?.Invoke(false);
    }

    protected override void MarkDisconnected(string reason)
    {
        _statusStore?.MarkDisconnected(DeviceId, _plcCode, DeviceName, reason);
        _connectionStateChanged?.Invoke(false);
    }
}
