using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using MediatR;

namespace IIoT.Edge.Application.Abstractions.Events;

/// <summary>
/// 产能变更通知事件。
/// 由 CapacityConsumer 在每个电芯完成后发布。
/// 订阅方在收到通知后刷新界面展示。
/// </summary>
public class CapacityUpdatedNotification : INotification
{
    public required TodayCapacity Snapshot { get; init; }
}

