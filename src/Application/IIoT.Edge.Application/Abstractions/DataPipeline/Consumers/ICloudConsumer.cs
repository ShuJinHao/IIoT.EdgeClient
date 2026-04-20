using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;

/// <summary>
/// 云端上报消费者接口。
/// </summary>
public interface ICloudConsumer : ICellDataConsumer
{
    Task<CloudCallResult> ProcessWithResultAsync(CellCompletedRecord record);
}
