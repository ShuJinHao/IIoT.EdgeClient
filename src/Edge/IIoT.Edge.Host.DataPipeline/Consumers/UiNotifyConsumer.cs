using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Consumers;
using IIoT.Edge.Module.Contracts.Events;
using MediatR;

namespace IIoT.Edge.Host.DataPipeline.Consumers;

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
    public IIoT.Edge.Module.Contracts.DataPipeline.ConsumerFailureMode FailureMode
        => IIoT.Edge.Module.Contracts.DataPipeline.ConsumerFailureMode.BestEffort;
    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.None;

    public UiNotifyConsumer(
        IPublisher publisher,
        ILogService logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            await _publisher.Publish(new CellCompletedEvent(record), cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"[UI] 通知发布失败，{record.CellData.DisplayLabel}，{ex.Message}");
            return true; // 界面通知失败不阻塞主流程，也不进入补传
        }
    }
}
