using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Module.Contracts.Runtime;

namespace IIoT.Edge.Application.Features.Production.Monitor;

/// <summary>
/// 监控快照投影构建器，负责把运行上下文、PLC 状态和配置设备投影为 UI 可读快照。
/// </summary>
public interface IMonitorSnapshotProjectionBuilder
{
    DeviceMonitorSnapshot BuildContextSnapshot(
        ProductionContext context,
        PlcConnectionRuntimeSnapshot? runtimeStatus,
        DevicePluginPlcSnapshot? configuredDevice,
        EdgeSyncDiagnosticsSnapshot diagnostics,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice);

    DeviceMonitorSnapshot BuildRuntimeOnlySnapshot(
        PlcConnectionRuntimeSnapshot runtimeStatus,
        DevicePluginPlcSnapshot? configuredDevice,
        EdgeSyncDiagnosticsSnapshot diagnostics,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice);

    DeviceMonitorSnapshot BuildConfiguredDeviceSnapshot(
        DevicePluginPlcSnapshot device,
        EdgeSyncDiagnosticsSnapshot diagnostics,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice);
}
