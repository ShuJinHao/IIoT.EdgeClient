using IIoT.Edge.SharedKernel.DataPipeline;
using MediatR;

namespace IIoT.Edge.Application.Abstractions.Events;

/// <summary>
/// 电芯完成通知事件。
///
/// 由 UiNotifyConsumer 在数据管道消费完成后发布。
/// 订阅方可以据此刷新界面，或触发其他本地通知流程。
///
/// 实现 INotification 表示这是一个通知型事件，可由多个处理器同时订阅。
/// </summary>
public record CellCompletedEvent(CellCompletedRecord Record) : INotification;
