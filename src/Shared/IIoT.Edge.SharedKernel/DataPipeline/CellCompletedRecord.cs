using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.SharedKernel.DataPipeline;

/// <summary>
/// 电芯完成记录。
/// 作为数据管道中传递单次完工结果的载体。
/// </summary>
public class CellCompletedRecord
{
    /// <summary>
    /// 电芯生产数据。
    /// 具体子类由工序决定，消费者可根据类型或 <c>ProcessType</c> 判断。
    /// </summary>
    public CellDataBase CellData { get; set; } = null!;
}
