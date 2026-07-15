using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

public interface IFallbackBufferStore<TFallbackRecord>
    where TFallbackRecord : IFallbackRecord
{
    Task SaveAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage,
        CancellationToken cancellationToken = default);

    Task<List<TFallbackRecord>> GetPendingAsync(int batchSize = 50);

    Task MovePendingToRetryAsync(IEnumerable<long> ids);

    Task DeleteBatchAsync(IEnumerable<long> ids);

    Task<int> GetCountAsync();
}
