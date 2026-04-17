using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.Events;
using MediatR;

namespace IIoT.Edge.Runtime.DataPipeline.Consumers;

/// <summary>
/// 界面通知消费者
/// 
/// 消费链最后一环，顺序为 50。
/// 职责单一：通过 MediatR 发布 CellCompletedEvent，通知界面刷新。
/// 
/// 产能统计已迁移至 CapacityConsumer，顺序为 10。
/// </summary>
public class UiNotifyConsumer : IUiNotifyConsumer
{
    private readonly IPublisher _publisher;
    private readonly ILogService _logger;

    public string Name => "UI";
    public int Order => 50;
    public IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode FailureMode
        => IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode.BestEffort;
    public string? RetryChannel => null;

    public UiNotifyConsumer(
        IPublisher publisher,
        ILogService logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record)
    {
        try
        {
            await _publisher.Publish(new CellCompletedEvent(record));
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"[UI] 通知发布失败，{record.CellData.DisplayLabel}，{ex.Message}");
            return true; // 界面通知失败不阻塞主流程，也不进入补传
        }
    }
}
