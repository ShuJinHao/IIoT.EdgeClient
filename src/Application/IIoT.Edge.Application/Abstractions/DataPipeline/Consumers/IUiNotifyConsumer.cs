namespace IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;

/// <summary>
/// 界面通知消费者接口。
///
/// 负责在数据消费完成后通知界面刷新，不承担产能统计职责。
/// </summary>
public interface IUiNotifyConsumer : ICellDataConsumer;
