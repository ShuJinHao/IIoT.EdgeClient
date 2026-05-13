namespace IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;

/// <summary>
/// 产能统计消费者标记接口。
///
/// 在依赖注入中既作为 ICapacityConsumer 注册，
/// 也会映射为通用的 ICellDataConsumer 参与消费链执行。
/// </summary>
public interface ICapacityConsumer : ICellDataConsumer
{
}
