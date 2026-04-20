using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

public sealed class ClaimedFailedCellBatch
{
    public string ClaimToken { get; set; } = string.Empty;
    public List<FailedCellRecord> Records { get; set; } = new();
}

public interface ICloudRetryRecordStore
{
    Task SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage);

    Task<List<FailedCellRecord>> GetPendingAsync(int batchSize = 10);

    Task<ClaimedFailedCellBatch?> ClaimPendingBatchAsync(int batchSize = 10);

    Task DeleteClaimedBatchAsync(string claimToken);

    Task ReleaseClaimAsync(string claimToken);

    Task DeleteAsync(long id);

    Task UpdateRetryAsync(long id, int retryCount, string errorMessage, DateTime nextRetryTime);

    Task<int> GetCountAsync();

    Task<int> GetCountAsync(string processType);

    Task ResetAllAbandonedAsync();

    Task<int> DeleteExpiredAbandonedAsync(DateTime olderThanUtc);
}
