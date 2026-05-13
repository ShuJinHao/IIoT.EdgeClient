using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

public interface ICloudFallbackBufferStore
{
    Task SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage);
    Task<List<CloudFallbackRecord>> GetPendingAsync(int batchSize = 50);
    Task MovePendingToRetryAsync(IEnumerable<long> ids);
    Task DeleteBatchAsync(IEnumerable<long> ids);
    Task<int> GetCountAsync();
}
