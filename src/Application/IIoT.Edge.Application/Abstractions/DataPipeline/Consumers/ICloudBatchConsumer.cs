using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;

/// <summary>
/// 云端批量上报能力接口，主要用于离线补传时的批次上传。
/// </summary>
public interface ICloudBatchConsumer
{
    Task<CloudCallResult> ProcessBatchAsync(IReadOnlyList<CellCompletedRecord> records);
}
