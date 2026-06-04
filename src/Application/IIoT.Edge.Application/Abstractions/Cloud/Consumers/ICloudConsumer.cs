using IIoT.Edge.SharedKernel.DataPipeline;

using IIoT.Edge.Application.Abstractions.DataPipeline;
namespace IIoT.Edge.Application.Abstractions.Cloud;

/// <summary>
/// 云端上报消费者接口。
/// </summary>
public interface ICloudConsumer : ICellDataConsumer
{
    Task<CloudCallResult> ProcessWithResultAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default);
}
