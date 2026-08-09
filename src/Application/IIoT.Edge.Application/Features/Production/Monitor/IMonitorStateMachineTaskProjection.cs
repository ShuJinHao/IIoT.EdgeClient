using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Common.Plugins;

namespace IIoT.Edge.Application.Features.Production.Monitor;

/// <summary>
/// 监控页状态机任务行投影器，负责把任务绑定和当前 step 状态合成为诊断行。
/// </summary>
public interface IMonitorStateMachineTaskProjection
{
    IReadOnlyList<MonitorStateMachineTaskSnapshot> BuildRows(
        DevicePluginPlcSnapshot? device,
        IReadOnlyDictionary<string, int> stepStates,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice);
}
