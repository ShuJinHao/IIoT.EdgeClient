using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Host.DataPipeline.Services;

internal sealed class IngressOverflowPersistence : IIngressOverflowPersistence
{
    private readonly List<ICellDataConsumer> _durableConsumers;
    private readonly int _bestEffortConsumerCount;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly DataPipelineCascadingPersistenceWriter _persistenceWriter;
    private readonly ILogService _logger;

    public IngressOverflowPersistence(
        IEnumerable<ICellDataConsumer> consumers,
        ILocalSystemRuntimeConfigService runtimeConfig,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        DataPipelineCascadingPersistenceWriter persistenceWriter,
        ILogService logger)
    {
        var consumerList = consumers.OrderBy(x => x.Order).ToList();
        _durableConsumers = consumerList
            .Where(x => x.FailureMode == ConsumerFailureMode.Durable)
            .ToList();
        _bestEffortConsumerCount = consumerList.Count - _durableConsumers.Count;
        _runtimeConfig = runtimeConfig;
        _criticalFallbackWriter = criticalFallbackWriter;
        _persistenceWriter = persistenceWriter;
        _logger = logger;
    }

    public async ValueTask<DataPipelineEnqueueResult> PersistOverflowAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default)
    {
        var persistedTargetCount = 0;

        foreach (var consumer in _durableConsumers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (consumer.RetryChannel == DataPipelineRetryChannel.None)
            {
                var details =
                    $"[DataPipeline] 队列溢出时跳过关键消费者 {consumer.Name}，原因：未配置 RetryChannel。ProcessType={record.CellData.ProcessType}。";
                _logger.Error(details);
                _criticalFallbackWriter.Write("DataPipeline.Overflow.InvalidRetryChannel", details);
                continue;
            }

            if (IsChannelDisabled(consumer.RetryChannel))
            {
                _logger.Warn(
                    $"[DataPipeline] 队列溢出时跳过已屏蔽外部通道 {consumer.Name}，工序={record.CellData.ProcessType}。");
                continue;
            }

            var persisted = await PersistForChannelAsync(record, consumer).ConfigureAwait(false);
            if (persisted)
            {
                persistedTargetCount++;
            }
        }

        if (_bestEffortConsumerCount > 0)
        {
            _logger.Warn(
                $"[DataPipeline] 队列溢出时跳过 {_bestEffortConsumerCount} 个非关键消费者，工序={record.CellData.ProcessType}。");
        }

        return DataPipelineEnqueueResult.OverflowPersisted(persistedTargetCount, _bestEffortConsumerCount);
    }

    private async Task<bool> PersistForChannelAsync(
        CellCompletedRecord record,
        ICellDataConsumer consumer)
    {
        if (consumer.RetryChannel is not DataPipelineRetryChannel.Cloud and not DataPipelineRetryChannel.Mes)
        {
            var details =
                $"[DataPipeline] 队列溢出时发现不支持的补偿链路：{consumer.RetryChannel}，消费者={consumer.Name}。";
            _logger.Error(details);
            _criticalFallbackWriter.Write("DataPipeline.Overflow.UnsupportedRetryChannel", details);
            return false;
        }

        return await _persistenceWriter.PersistAsync(
                record,
                consumer.RetryChannel,
                consumer.Name,
                "queue_overflow",
                "ingress_overflow",
                DeadLetterStage.FallbackPersist)
            .ConfigureAwait(false);
    }

    private bool IsChannelDisabled(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => !_runtimeConfig.Current.CloudUploadEnabled,
            DataPipelineRetryChannel.Mes => !_runtimeConfig.Current.MesUploadEnabled,
            _ => false
        };
}
