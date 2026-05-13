using IIoT.Edge.Application.Abstractions.Tasks;

namespace IIoT.Edge.Application.Abstractions.Plc;

/// <summary>
/// PLC 任务契约。
/// 继承 IBackgroundTask，专用于与 PLC 设备交互的任务。
/// </summary>
public interface IPlcTask : IBackgroundTask
{
}
