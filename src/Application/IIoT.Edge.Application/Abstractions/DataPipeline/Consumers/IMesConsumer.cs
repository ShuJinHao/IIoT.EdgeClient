namespace IIoT.Edge.Application.Abstractions.DataPipeline;

/// <summary>
/// MES 上报消费者接口。
///
/// 不同客户的 MES 系统在数据格式、签名方式和接口协议上可能存在差异，
/// 具体实现按客户需求提供。
/// </summary>
public interface IMesConsumer : ICellDataConsumer;
