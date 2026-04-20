using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

internal sealed class IngressOverflowPersistence : IIngressOverflowPersistence
{
    private readonly List<ICellDataConsumer> _durableConsumers;
    private readonly int _bestEffortConsumerCount;
    private readonly ICloudRetryRecordStore _cloudRetryStore;
    private readonly IMesRetryRecordStore _mesRetryStore;
    private readonly ICloudFallbackBufferStore _cloudFallbackStore;
    private readonly IMesFallbackBufferStore _mesFallbackStore;
    private readonly ICloudDeadLetterStore _cloudDeadLetterStore;
    private readonly IMesDeadLetterStore _mesDeadLetterStore;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly ILogService _logger;
    private readonly DataPipelineCapacityGuard? _capacityGuard;

    public IngressOverflowPersistence(
        IEnumerable<ICellDataConsumer> consumers,
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        ICloudFallbackBufferStore cloudFallbackStore,
        IMesFallbackBufferStore mesFallbackStore,
        ICloudDeadLetterStore cloudDeadLetterStore,
        IMesDeadLetterStore mesDeadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        ILogService logger,
        DataPipelineCapacityGuard? capacityGuard = null)
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
        _criticalFallbackWriter = criticalFallbackWriter;
        _logger = logger;
        _capacityGuard = capacityGuard;
    }

    public async ValueTask<DataPipelineEnqueueResult> PersistOverflowAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default)
    {
        var persistedTargetCount = 0;

        foreach (var consumer in _durableConsumers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(consumer.RetryChannel))
            {
                var details =
                    $"[DataPipeline] Overflow persistence skipped durable consumer '{consumer.Name}' because RetryChannel is empty. ProcessType={record.CellData.ProcessType}.";
                _logger.Error(details);
                _criticalFallbackWriter.Write("DataPipeline.Overflow.InvalidRetryChannel", details);
                continue;
            }

            var persisted = await PersistForChannelAsync(
                record,
                consumer.Name,
                consumer.RetryChannel,
                DeadLetterStage.QueueOverflowPersist).ConfigureAwait(false);

            if (persisted)
            {
                persistedTargetCount++;
            }
        }

        if (_bestEffortConsumerCount > 0)
        {
            _logger.Warn(
                $"[DataPipeline] Queue overflow skipped {_bestEffortConsumerCount} best-effort consumer(s) for {record.CellData.ProcessType}.");
        }

        return DataPipelineEnqueueResult.OverflowPersisted(persistedTargetCount, _bestEffortConsumerCount);
    }

    private async Task<bool> PersistForChannelAsync(
        CellCompletedRecord record,
        string failedTarget,
        string retryChannel,
        DeadLetterStage stage)
    {
        if (string.Equals(retryChannel, "Cloud", StringComparison.OrdinalIgnoreCase))
        {
            return await PersistCloudAsync(record, failedTarget, stage).ConfigureAwait(false);
        }

        if (string.Equals(retryChannel, "MES", StringComparison.OrdinalIgnoreCase))
        {
            return await PersistMesAsync(record, failedTarget, stage).ConfigureAwait(false);
        }

        var details =
            $"[DataPipeline] Overflow persistence found unsupported retry channel '{retryChannel}' for consumer '{failedTarget}'.";
        _logger.Error(details);
        _criticalFallbackWriter.Write("DataPipeline.Overflow.UnsupportedRetryChannel", details);
        return false;
    }

    private async Task<bool> PersistCloudAsync(
        CellCompletedRecord record,
        string failedTarget,
        DeadLetterStage stage)
    {
        const string sourceTable = "ingress_overflow";
        const string errorMessage = "queue_overflow";
        var retryBlockedReason = _capacityGuard is null
            ? null
            : await _capacityGuard.GetCloudRetryBlockReasonAsync(record.CellData.ProcessType).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(retryBlockedReason))
        {
            return await TryPersistCloudDeadLetterAsync(
                record,
                failedTarget,
                sourceTable,
                sourceRecordId: null,
                DeadLetterStage.CapacityBlocked,
                BuildCapacityBlockedFailureReason(CapacityBlockedChannel.Retry, retryBlockedReason),
                exception: null).ConfigureAwait(false);
        }

        try
        {
            await _cloudRetryStore.SaveAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
            _logger.Error(
                $"[DataPipeline] Queue overflow persisted {record.CellData.DisplayLabel} directly to Cloud retry store.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"[DataPipeline] Queue overflow failed to persist Cloud retry record for {record.CellData.DisplayLabel}: {ex.Message}");

            var fallbackBlockedReason = _capacityGuard is null
                ? null
                : await _capacityGuard.GetCloudFallbackBlockReasonAsync(record.CellData.ProcessType).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(fallbackBlockedReason))
            {
                return await TryPersistCloudDeadLetterAsync(
                    record,
                    failedTarget,
                    sourceTable,
                    sourceRecordId: null,
                    DeadLetterStage.CapacityBlocked,
                    BuildCapacityBlockedFailureReason(CapacityBlockedChannel.Fallback, fallbackBlockedReason),
                    exception: null).ConfigureAwait(false);
            }

            try
            {
                await _cloudFallbackStore.SaveAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
                _logger.Error(
                    $"[DataPipeline] Queue overflow persisted {record.CellData.DisplayLabel} to Cloud fallback buffer.");
                return true;
            }
            catch (Exception fallbackEx)
            {
                return await TryPersistCloudDeadLetterAsync(
                    record,
                    failedTarget,
                    sourceTable,
                    sourceRecordId: null,
                    DeadLetterStage.FallbackPersist,
                    $"Cloud retry save failed: {ex.Message}; Cloud fallback save failed: {fallbackEx.Message}",
                    fallbackEx).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> PersistMesAsync(
        CellCompletedRecord record,
        string failedTarget,
        DeadLetterStage stage)
    {
        const string sourceTable = "ingress_overflow";
        const string errorMessage = "queue_overflow";
        var retryBlockedReason = _capacityGuard is null
            ? null
            : await _capacityGuard.GetMesRetryBlockReasonAsync(record.CellData.ProcessType).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(retryBlockedReason))
        {
            return await TryPersistMesDeadLetterAsync(
                record,
                failedTarget,
                sourceTable,
                sourceRecordId: null,
                DeadLetterStage.CapacityBlocked,
                BuildCapacityBlockedFailureReason(CapacityBlockedChannel.Retry, retryBlockedReason),
                exception: null).ConfigureAwait(false);
        }

        try
        {
            await _mesRetryStore.SaveAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
            _logger.Error(
                $"[DataPipeline] Queue overflow persisted {record.CellData.DisplayLabel} directly to MES retry store.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"[DataPipeline] Queue overflow failed to persist MES retry record for {record.CellData.DisplayLabel}: {ex.Message}");

            var fallbackBlockedReason = _capacityGuard is null
                ? null
                : await _capacityGuard.GetMesFallbackBlockReasonAsync(record.CellData.ProcessType).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(fallbackBlockedReason))
            {
                return await TryPersistMesDeadLetterAsync(
                    record,
                    failedTarget,
                    sourceTable,
                    sourceRecordId: null,
                    DeadLetterStage.CapacityBlocked,
                    BuildCapacityBlockedFailureReason(CapacityBlockedChannel.Fallback, fallbackBlockedReason),
                    exception: null).ConfigureAwait(false);
            }

            try
            {
                await _mesFallbackStore.SaveAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
                _logger.Error(
                    $"[DataPipeline] Queue overflow persisted {record.CellData.DisplayLabel} to MES fallback buffer.");
                return true;
            }
            catch (Exception fallbackEx)
            {
                return await TryPersistMesDeadLetterAsync(
                    record,
                    failedTarget,
                    sourceTable,
                    sourceRecordId: null,
                    DeadLetterStage.FallbackPersist,
                    $"MES retry save failed: {ex.Message}; MES fallback save failed: {fallbackEx.Message}",
                    fallbackEx).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> TryPersistCloudDeadLetterAsync(
        CellCompletedRecord record,
        string failedTarget,
        string sourceTable,
        long? sourceRecordId,
        DeadLetterStage stage,
        string failureReason,
        Exception? exception)
    {
        try
        {
            await _cloudDeadLetterStore.SaveAsync(BuildDeadLetterRecord(
                record,
                failedTarget,
                sourceTable,
                sourceRecordId,
                stage,
                failureReason)).ConfigureAwait(false);
            _logger.Fatal(
                $"[DataPipeline] Queue overflow moved {record.CellData.DisplayLabel} into Cloud dead-letter store.");
            return true;
        }
        catch (Exception deadLetterEx)
        {
            _criticalFallbackWriter.Write(
                "DataPipeline.CloudDeadLetterPersistFailed",
                $"{failureReason}; Cloud dead-letter save failed: {deadLetterEx.Message}",
                exception);
            return false;
        }
    }

    private async Task<bool> TryPersistMesDeadLetterAsync(
        CellCompletedRecord record,
        string failedTarget,
        string sourceTable,
        long? sourceRecordId,
        DeadLetterStage stage,
        string failureReason,
        Exception? exception)
    {
        try
        {
            await _mesDeadLetterStore.SaveAsync(BuildDeadLetterRecord(
                record,
                failedTarget,
                sourceTable,
                sourceRecordId,
                stage,
                failureReason)).ConfigureAwait(false);
            _logger.Fatal(
                $"[DataPipeline] Queue overflow moved {record.CellData.DisplayLabel} into MES dead-letter store.");
            return true;
        }
        catch (Exception deadLetterEx)
        {
            _criticalFallbackWriter.Write(
                "DataPipeline.MesDeadLetterPersistFailed",
                $"{failureReason}; MES dead-letter save failed: {deadLetterEx.Message}",
                exception);
            return false;
        }
    }

    private static DeadLetterRecord BuildDeadLetterRecord(
        CellCompletedRecord record,
        string failedTarget,
        string sourceTable,
        long? sourceRecordId,
        DeadLetterStage stage,
        string failureReason)
        => new()
        {
            ProcessType = record.CellData.ProcessType,
            CellDataJson = CellDataJsonSerializer.Serialize(record.CellData),
            FailedTarget = failedTarget,
            SourceTable = sourceTable,
            SourceRecordId = sourceRecordId,
            FailureStage = stage.ToString(),
            FailureReason = failureReason,
            CreatedAt = DateTime.UtcNow
        };

    private static string BuildCapacityBlockedFailureReason(
        CapacityBlockedChannel channel,
        string blockedReason)
        => $"capacity_blocked:{channel.ToString().ToLowerInvariant()}:{blockedReason}";
}
