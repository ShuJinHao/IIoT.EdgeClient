using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.Cloud;

/// <summary>
/// 云端批量上报能力接口，主要用于离线补传时的批次上传。
/// </summary>
public interface ICloudBatchConsumer
{
    CloudCallResult ValidateBatchRecord(CellCompletedRecord record);

    Task<CloudCallResult> ProcessBatchAsync(
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default);
}
