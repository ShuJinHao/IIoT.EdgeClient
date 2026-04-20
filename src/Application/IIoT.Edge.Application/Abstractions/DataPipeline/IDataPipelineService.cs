using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline;

/// <summary>
/// 数据管道入队服务。
/// 
/// 由上游任务从 Context 读取电芯数据并封装为 CellCompletedRecord，
/// 再调用 Enqueue 推入内存队列，随后移除已完成的电芯数据。
/// </summary>
public interface IDataPipelineService
{
    /// <summary>
    /// 将打包好的电芯完成记录推入消费队列。
    /// </summary>
    ValueTask<DataPipelineEnqueueResult> EnqueueAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试从队列中取出一条待处理记录。
    /// </summary>
    bool TryDequeue(out CellCompletedRecord? record);

    /// <summary>
    /// 等待队列中出现可消费数据。
    /// </summary>
    ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 当前内存队列中的待处理项数量，供界面监控使用。
    /// </summary>
    int PendingCount { get; }

    int OverflowCount { get; }

    int SpillCount { get; }
}
