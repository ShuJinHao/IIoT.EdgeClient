namespace IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;

/// <summary>
/// Excel 本地落盘消费者接口。
///
/// 不同机台或客户可能需要不同的 Excel 列结构、表头名称或工作表组织方式，
/// 具体实现由对应基础设施层提供。
/// </summary>
public interface IExcelConsumer : ICellDataConsumer;
