using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Runtime.DataPipeline.Tasks;

public class ProcessQueueTask : ScheduledTaskBase
{
    private const int MaxDrainBatchSize = 100;

    private readonly IDataPipelineService _pipelineService;
    private readonly List<ICellDataConsumer> _consumers;
    private readonly ICloudRetryRecordStore _cloudRetryStore;
    private readonly IMesRetryRecordStore _mesRetryStore;
    private readonly ICloudFallbackBufferStore _cloudFallbackStore;
    private readonly IMesFallbackBufferStore _mesFallbackStore;
    private readonly ICloudDeadLetterStore _cloudDeadLetterStore;
    private readonly IMesDeadLetterStore _mesDeadLetterStore;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly DataPipelineCapacityGuard? _capacityGuard;

    public override string TaskName => "ProcessQueueTask";
    protected override int ExecuteInterval => 0;

    public ProcessQueueTask(
        ILogService logger,
        IDataPipelineService pipelineService,
        IEnumerable<ICellDataConsumer> consumers,
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        ICloudFallbackBufferStore cloudFallbackStore,
        IMesFallbackBufferStore mesFallbackStore,
        ICloudDeadLetterStore cloudDeadLetterStore,
        IMesDeadLetterStore mesDeadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        DataPipelineCapacityGuard? capacityGuard = null)
        : base(logger)
    {
        _pipelineService = pipelineService;
        _cloudRetryStore = cloudRetryStore;
        _mesRetryStore = mesRetryStore;
        _cloudFallbackStore = cloudFallbackStore;
        _mesFallbackStore = mesFallbackStore;
        _cloudDeadLetterStore = cloudDeadLetterStore;
        _mesDeadLetterStore = mesDeadLetterStore;
        _criticalFallbackWriter = criticalFallbackWriter;
        _consumers = consumers.OrderBy(c => c.Order).ToList();
        _capacityGuard = capacityGuard;
    }

    protected override async Task ExecuteAsync()
    {
        var drainedCount = 0;
        while (drainedCount < MaxDrainBatchSize
               && _pipelineService.TryDequeue(out var record)
               && record is not null)
        {
            await ProcessOneAsync(record).ConfigureAwait(false);
            drainedCount++;
        }
    }

    protected override async Task WaitForNextIterationAsync(CancellationToken ct)
    {
        await _pipelineService.WaitToReadAsync(ct).ConfigureAwait(false);
    }

    private async Task ProcessOneAsync(CellCompletedRecord record)
    {
        var label = record.CellData.DisplayLabel;
        Logger.Info($"[{record.CellData.ProcessType}] Start processing {label}");

        foreach (var consumer in _consumers)
        {
            try
            {
                var success = await consumer.ProcessAsync(record).ConfigureAwait(false);
                if (!success)
                {
                    await HandleFailureAsync(record, consumer, "Consumer returned false.").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(record, consumer, ex.Message).ConfigureAwait(false);
            }
        }

        Logger.Info($"[{record.CellData.ProcessType}] {label} processing chain completed.");
    }

    private async Task HandleFailureAsync(
        CellCompletedRecord record,
        ICellDataConsumer consumer,
        string errorMessage)
    {
        var label = record.CellData.DisplayLabel;

        if (consumer.FailureMode == ConsumerFailureMode.BestEffort)
        {
            Logger.Warn($"[{record.CellData.ProcessType}] {consumer.Name} failed for {label}: {errorMessage} (best-effort)");
            return;
        }

        if (string.IsNullOrWhiteSpace(consumer.RetryChannel))
        {
            var details =
                $"[{record.CellData.ProcessType}] Durable consumer {consumer.Name} failed for {label}, but RetryChannel is not configured.";
            Logger.Error(details);
            _criticalFallbackWriter.Write("DataPipeline.ProcessQueue.InvalidRetryChannel", details);
            return;
        }

        Logger.Warn(
            $"[{record.CellData.ProcessType}] {consumer.Name} failed for {label}. Move to retry channel {consumer.RetryChannel}.");

        if (string.Equals(consumer.RetryChannel, "Cloud", StringComparison.OrdinalIgnoreCase))
        {
            await PersistCloudFailureAsync(record, consumer.Name, errorMessage).ConfigureAwait(false);
            return;
        }

        if (string.Equals(consumer.RetryChannel, "MES", StringComparison.OrdinalIgnoreCase))
        {
            await PersistMesFailureAsync(record, consumer.Name, errorMessage).ConfigureAwait(false);
            return;
        }

        var unsupportedDetails =
            $"[{record.CellData.ProcessType}] Unsupported retry channel {consumer.RetryChannel} for {consumer.Name}.";
        Logger.Error(unsupportedDetails);
        _criticalFallbackWriter.Write("DataPipeline.ProcessQueue.UnsupportedRetryChannel", unsupportedDetails);
    }

    private async Task PersistCloudFailureAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage)
    {
        var label = record.CellData.DisplayLabel;
        var retryBlockedReason = _capacityGuard is null
            ? null
            : await _capacityGuard.GetCloudRetryBlockReasonAsync(record.CellData.ProcessType).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(retryBlockedReason))
        {
            await TryPersistCloudDeadLetterAsync(
                record,
                failedTarget,
                sourceTable: "failed_cloud_records",
                sourceRecordId: null,
                DeadLetterStage.CapacityBlocked,
                BuildCapacityBlockedFailureReason(CapacityBlockedChannel.Retry, retryBlockedReason),
                exception: null).ConfigureAwait(false);
            return;
        }

        try
        {
            await _cloudRetryStore.SaveAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error($"[{record.CellData.ProcessType}] Save retry record failed for {label}: {ex.Message}");

            var fallbackBlockedReason = _capacityGuard is null
                ? null
                : await _capacityGuard.GetCloudFallbackBlockReasonAsync(record.CellData.ProcessType).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(fallbackBlockedReason))
            {
                await TryPersistCloudDeadLetterAsync(
                    record,
                    failedTarget,
                    sourceTable: "failed_cloud_records",
                    sourceRecordId: null,
                    DeadLetterStage.CapacityBlocked,
                    BuildCapacityBlockedFailureReason(CapacityBlockedChannel.Fallback, fallbackBlockedReason),
                    exception: null).ConfigureAwait(false);
                return;
            }

            try
            {
                await _cloudFallbackStore.SaveAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
                Logger.Error(
                    $"[{record.CellData.ProcessType}] Main retry store unavailable. Persisted {label} to Cloud fallback buffer.");
            }
            catch (Exception fallbackEx)
            {
                await TryPersistCloudDeadLetterAsync(
                    record,
                    failedTarget,
                    sourceTable: "failed_cloud_records",
                    sourceRecordId: null,
                    DeadLetterStage.FallbackPersist,
                    $"Cloud retry save failed: {ex.Message}; Cloud fallback save failed: {fallbackEx.Message}",
                    fallbackEx).ConfigureAwait(false);
            }
        }
    }

    private async Task PersistMesFailureAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage)
    {
        var label = record.CellData.DisplayLabel;
        var retryBlockedReason = _capacityGuard is null
            ? null
            : await _capacityGuard.GetMesRetryBlockReasonAsync(record.CellData.ProcessType).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(retryBlockedReason))
        {
            await TryPersistMesDeadLetterAsync(
                record,
                failedTarget,
                sourceTable: "failed_mes_records",
                sourceRecordId: null,
                DeadLetterStage.CapacityBlocked,
                BuildCapacityBlockedFailureReason(CapacityBlockedChannel.Retry, retryBlockedReason),
                exception: null).ConfigureAwait(false);
            return;
        }

        try
        {
            await _mesRetryStore.SaveAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error($"[{record.CellData.ProcessType}] Save retry record failed for {label}: {ex.Message}");

            var fallbackBlockedReason = _capacityGuard is null
                ? null
                : await _capacityGuard.GetMesFallbackBlockReasonAsync(record.CellData.ProcessType).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(fallbackBlockedReason))
            {
                await TryPersistMesDeadLetterAsync(
                    record,
                    failedTarget,
                    sourceTable: "failed_mes_records",
                    sourceRecordId: null,
                    DeadLetterStage.CapacityBlocked,
                    BuildCapacityBlockedFailureReason(CapacityBlockedChannel.Fallback, fallbackBlockedReason),
                    exception: null).ConfigureAwait(false);
                return;
            }

            try
            {
                await _mesFallbackStore.SaveAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
                Logger.Error(
                    $"[{record.CellData.ProcessType}] Main retry store unavailable. Persisted {label} to MES fallback buffer.");
            }
            catch (Exception fallbackEx)
            {
                await TryPersistMesDeadLetterAsync(
                    record,
                    failedTarget,
                    sourceTable: "failed_mes_records",
                    sourceRecordId: null,
                    DeadLetterStage.FallbackPersist,
                    $"MES retry save failed: {ex.Message}; MES fallback save failed: {fallbackEx.Message}",
                    fallbackEx).ConfigureAwait(false);
            }
        }
    }

    private async Task TryPersistCloudDeadLetterAsync(
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
            Logger.Fatal(
                $"[{record.CellData.ProcessType}] Cloud dead-letter store captured {record.CellData.DisplayLabel} after retry persistence failure.");
        }
        catch (Exception deadLetterEx)
        {
            _criticalFallbackWriter.Write(
                "DataPipeline.ProcessQueue.CloudDeadLetterPersistFailed",
                $"{failureReason}; Cloud dead-letter save failed: {deadLetterEx.Message}",
                exception);
        }
    }

    private async Task TryPersistMesDeadLetterAsync(
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
            Logger.Fatal(
                $"[{record.CellData.ProcessType}] MES dead-letter store captured {record.CellData.DisplayLabel} after retry persistence failure.");
        }
        catch (Exception deadLetterEx)
        {
            _criticalFallbackWriter.Write(
                "DataPipeline.ProcessQueue.MesDeadLetterPersistFailed",
                $"{failureReason}; MES dead-letter save failed: {deadLetterEx.Message}",
                exception);
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
