using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

/// <summary>
/// 失败记录存储接口。
/// 
/// ProcessQueueTask 在消费失败时调用 SaveAsync 写入 SQLite。
/// RetryTask 按通道定时调用 GetPendingAsync 读取待重试记录。
/// 重试成功后再调用 DeleteAsync 删除记录。
/// </summary>
public interface IFailedRecordStore
{
    /// <summary>
    /// 存入一条失败记录。
    /// </summary>
    /// <param name="channel">补传通道，例如 "Cloud" 或 "MES"。</param>
    Task SaveAsync(CellCompletedRecord record, string failedTarget,
                   string errorMessage, string channel);

    /// <summary>
    /// 获取待重试记录，仅返回指定通道且 NextRetryTime 已到期的数据。
    /// </summary>
    Task<List<FailedCellRecord>> GetPendingAsync(string channel, int batchSize = 10);

    /// <summary>
    /// 重试成功后删除记录。
    /// </summary>
    Task DeleteAsync(long id);

    /// <summary>
    /// 重试失败后更新重试次数和下次重试时间。
    /// </summary>
    Task UpdateRetryAsync(long id, int retryCount, string errorMessage, DateTime nextRetryTime);

    /// <summary>
    /// 获取当前失败记录总数，供界面监控使用。
    /// </summary>
    Task<int> GetCountAsync();

    /// <summary>
    /// 按通道获取失败记录总数。
    /// </summary>
    Task<int> GetCountAsync(string channel);

    /// <summary>
    /// 按通道和工序获取失败记录总数。
    /// </summary>
    Task<int> GetCountAsync(string channel, string processType);

    /// <summary>
    /// 将所有 Abandoned 记录重置为可重试，供界面“全部重传”操作使用。
    /// </summary>
    Task ResetAllAbandonedAsync();
}
