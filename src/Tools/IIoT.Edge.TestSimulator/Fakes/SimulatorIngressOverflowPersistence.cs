using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.TestSimulator.Fakes;

public sealed class SimulatorIngressOverflowPersistence : IIngressOverflowPersistence
{
    private readonly List<ICellDataConsumer> _durableConsumers;
    private readonly int _bestEffortConsumerCount;
    private readonly ICloudRetryRecordStore _cloudRetryStore;
    private readonly IMesRetryRecordStore _mesRetryStore;
    private readonly ICloudFallbackBufferStore _cloudFallbackStore;
    private readonly IMesFallbackBufferStore _mesFallbackStore;
    private readonly ICloudDeadLetterStore _cloudDeadLetterStore;
    private readonly IMesDeadLetterStore _mesDeadLetterStore;
    private readonly ICriticalPersistenceFallbackWriter _criticalWriter;
    private readonly ILogService _logger;

    public SimulatorIngressOverflowPersistence(
        IEnumerable<ICellDataConsumer> consumers,
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        ICloudFallbackBufferStore cloudFallbackStore,
        IMesFallbackBufferStore mesFallbackStore,
        ICloudDeadLetterStore cloudDeadLetterStore,
        IMesDeadLetterStore mesDeadLetterStore,
        ICriticalPersistenceFallbackWriter criticalWriter,
        ILogService logger)
    {
        var consumerList = consumers.OrderBy(x => x.Order).ToList();
        _durableConsumers = consumerList
            .Where(x => x.FailureMode == ConsumerFailureMode.Durable)
            .ToList();
        _bestEffortConsumerCount = consumerList.Count - _durableConsumers.Count;
        _cloudRetryStore = cloudRetryStore;
        _mesRetryStore = mesRetryStore;
        _cloudFallbackStore = cloudFallbackStore;
        _mesFallbackStore = mesFallbackStore;
        _cloudDeadLetterStore = cloudDeadLetterStore;
        _mesDeadLetterStore = mesDeadLetterStore;
        _criticalWriter = criticalWriter;
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

            if (string.Equals(consumer.RetryChannel, "Cloud", StringComparison.OrdinalIgnoreCase))
            {
                if (await PersistCloudAsync(record, consumer.Name).ConfigureAwait(false))
                {
                    persistedTargetCount++;
                }

                continue;
            }

            if (string.Equals(consumer.RetryChannel, "MES", StringComparison.OrdinalIgnoreCase))
            {
                if (await PersistMesAsync(record, consumer.Name).ConfigureAwait(false))
                {
                    persistedTargetCount++;
                }

                continue;
            }

            _criticalWriter.Write(
                "Simulator.Overflow.UnsupportedRetryChannel",
                $"Unsupported retry channel '{consumer.RetryChannel}' for consumer '{consumer.Name}'.");
        }

        if (_bestEffortConsumerCount > 0)
        {
            _logger.Warn(
                $"[SimulatorOverflow] Skipped {_bestEffortConsumerCount} best-effort consumer(s) for {record.CellData.ProcessType}.");
        }

        return DataPipelineEnqueueResult.OverflowPersisted(persistedTargetCount, _bestEffortConsumerCount);
    }

    private async Task<bool> PersistCloudAsync(CellCompletedRecord record, string failedTarget)
    {
        try
        {
            await _cloudRetryStore.SaveAsync(record, failedTarget, "queue_overflow").ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                await _cloudFallbackStore.SaveAsync(record, failedTarget, "queue_overflow").ConfigureAwait(false);
                return true;
            }
            catch (Exception fallbackEx)
            {
                return await TryPersistCloudDeadLetterAsync(record, failedTarget, ex, fallbackEx).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> PersistMesAsync(CellCompletedRecord record, string failedTarget)
    {
        try
        {
            await _mesRetryStore.SaveAsync(record, failedTarget, "queue_overflow").ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                await _mesFallbackStore.SaveAsync(record, failedTarget, "queue_overflow").ConfigureAwait(false);
                return true;
            }
            catch (Exception fallbackEx)
            {
                return await TryPersistMesDeadLetterAsync(record, failedTarget, ex, fallbackEx).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> TryPersistCloudDeadLetterAsync(
        CellCompletedRecord record,
        string failedTarget,
        Exception primaryException,
        Exception fallbackException)
    {
        try
        {
            await _cloudDeadLetterStore.SaveAsync(new DeadLetterRecord
            {
                ProcessType = record.CellData.ProcessType,
                CellDataJson = CellDataJsonSerializer.Serialize(record.CellData),
                FailedTarget = failedTarget,
                SourceTable = "ingress_overflow",
                FailureStage = DeadLetterStage.QueueOverflowPersist.ToString(),
                FailureReason = $"Cloud retry save failed: {primaryException.Message}; Cloud fallback save failed: {fallbackException.Message}",
                CreatedAt = DateTime.UtcNow
            }).ConfigureAwait(false);
            return true;
        }
        catch (Exception deadLetterEx)
        {
            _criticalWriter.Write(
                "Simulator.Overflow.CloudDeadLetterPersistFailed",
                $"Cloud overflow dead-letter save failed: {deadLetterEx.Message}",
                deadLetterEx);
            return false;
        }
    }

    private async Task<bool> TryPersistMesDeadLetterAsync(
        CellCompletedRecord record,
        string failedTarget,
        Exception primaryException,
        Exception fallbackException)
    {
        try
        {
            await _mesDeadLetterStore.SaveAsync(new DeadLetterRecord
            {
                ProcessType = record.CellData.ProcessType,
                CellDataJson = CellDataJsonSerializer.Serialize(record.CellData),
                FailedTarget = failedTarget,
                SourceTable = "ingress_overflow",
                FailureStage = DeadLetterStage.QueueOverflowPersist.ToString(),
                FailureReason = $"MES retry save failed: {primaryException.Message}; MES fallback save failed: {fallbackException.Message}",
                CreatedAt = DateTime.UtcNow
            }).ConfigureAwait(false);
            return true;
        }
        catch (Exception deadLetterEx)
        {
            _criticalWriter.Write(
                "Simulator.Overflow.MesDeadLetterPersistFailed",
                $"MES overflow dead-letter save failed: {deadLetterEx.Message}",
                deadLetterEx);
            return false;
        }
    }
}
