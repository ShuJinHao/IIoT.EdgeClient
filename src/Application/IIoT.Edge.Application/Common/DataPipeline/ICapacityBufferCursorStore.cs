using IIoT.Edge.Module.Contracts.DataPipeline.Stores;

namespace IIoT.Edge.Application.Common.DataPipeline;

public interface ICapacityBufferCursorStore
{
    Task<ClaimedCapacityBufferCursorBatch?> ClaimHourlySummaryBatchAfterAsync(
        long afterRecordId,
        int batchSize = 200);
}

public sealed class ClaimedCapacityBufferCursorBatch
{
    public required string ClaimToken { get; init; }

    public required IReadOnlyList<BufferHourlySummaryDto> Summaries { get; init; }

    public required int ClaimedRecordCount { get; init; }

    public required long LastRecordId { get; init; }
}
