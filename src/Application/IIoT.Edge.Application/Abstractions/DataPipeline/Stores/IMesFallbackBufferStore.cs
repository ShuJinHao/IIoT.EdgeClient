using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

public interface IMesFallbackBufferStore
{
    Task SaveAsync(CellCompletedRecord record, string failedTarget, string errorMessage);
    Task<List<MesFallbackRecord>> GetPendingAsync(int batchSize = 50);
    Task DeleteBatchAsync(IEnumerable<long> ids);
    Task<int> GetCountAsync();
}
