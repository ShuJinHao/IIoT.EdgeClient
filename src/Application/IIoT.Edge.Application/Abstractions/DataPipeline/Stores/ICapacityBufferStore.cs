using IIoT.Edge.SharedKernel.DataPipeline.Capacity;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

/// <summary>
/// 离线缓冲汇总传输对象，按日期和班次聚合。
/// </summary>
public class BufferSummaryDto
{
    public string Date { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public int Total { get; set; }
    public int OkCount { get; set; }
    public int NgCount { get; set; }
}

/// <summary>
/// 离线缓冲的小时汇总传输对象，按日期、小时、分钟桶、班次和 PLC 聚合。
/// </summary>
public class BufferHourlySummaryDto
{
    public string Date { get; set; } = string.Empty;
    public int Hour { get; set; }
    public int MinuteBucket { get; set; }
    public string ShiftCode { get; set; } = string.Empty;
    public int Total { get; set; }
    public int OkCount { get; set; }
    public int NgCount { get; set; }
    public string PlcName { get; set; } = string.Empty;

}

public sealed class ClaimedCapacityBufferBatch
{
    public string ClaimToken { get; set; } = string.Empty;
    public List<BufferHourlySummaryDto> Summaries { get; set; } = new();
}

/// <summary>
/// 产能离线缓冲接口。
///
/// 仅在离线时写入，恢复在线后聚合补传，成功后清空。
/// 不承担长期存储职责，也不用于界面查询历史数据。
/// </summary>
public interface ICapacityBufferStore
{
    /// <summary>写入单条离线缓冲记录，每个电芯完成时实时调用。</summary>
    Task SaveAsync(CapacityRecord record);

    /// <summary>批量写入离线缓冲记录。</summary>
    Task SaveBatchAsync(IEnumerable<CapacityRecord> records);

    /// <summary>按日期和班次汇总，兼容旧版补传逻辑。</summary>
    Task<List<BufferSummaryDto>> GetShiftSummaryAsync();

    /// <summary>按日期、小时、分钟桶、班次和 PLC 汇总，供补传使用。</summary>
    Task<List<BufferHourlySummaryDto>> GetHourlySummaryAsync();

    /// <summary>认领一批待补传的小时汇总，避免并发清理造成重复或丢失。</summary>
    Task<ClaimedCapacityBufferBatch?> ClaimHourlySummaryBatchAsync(int batchSize = 200);

    /// <summary>删除某个认领批次中已经补传成功的单个汇总组。</summary>
    Task DeleteClaimedSummaryAsync(string claimToken, string date, int hour, int minuteBucket, string shiftCode, string plcName);

    /// <summary>释放认领批次，允许剩余数据下次重试。</summary>
    Task ReleaseClaimAsync(string claimToken);

    /// <summary>补传成功后清空全部缓冲记录。</summary>
    Task ClearAllAsync();

    /// <summary>获取缓冲区记录数，供诊断使用。</summary>
    Task<int> GetCountAsync();
}
