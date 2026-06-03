using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Application.Features.Production.Monitor;

/// <summary>
/// 监控页状态机任务行投影器，负责把任务绑定和当前 step 状态合成为诊断行。
/// </summary>
public interface IMonitorStateMachineTaskProjection
{
    IReadOnlyList<MonitorStateMachineTaskSnapshot> BuildRows(
        NetworkDeviceEntity? device,
        IReadOnlyDictionary<string, int> stepStates,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice);
}
