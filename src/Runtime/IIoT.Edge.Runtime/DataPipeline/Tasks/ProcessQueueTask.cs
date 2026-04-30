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
    private readonly DataPipelineCapacityGuard _capacityGuard;
    private readonly TimeSpan _consumerCallTimeout;

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
        DataPipelineCapacityGuard capacityGuard,
        DataPipelineRuntimeOptions? runtimeOptions = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(capacityGuard);

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
        _consumerCallTimeout = (runtimeOptions ?? new DataPipelineRuntimeOptions()).GetConsumerCallTimeout();
    }

    protected override async Task ExecuteAsync()
    {
        var drainedCount = 0;
        while (drainedCount < MaxDrainBatchSize
               && _pipelineService.TryDequeue(out var record)
               && record is not null)
        {
            await ProcessOneAsync(record, CurrentCancellationToken).ConfigureAwait(false);
            drainedCount++;
        }
    }

    protected override async Task WaitForNextIterationAsync(CancellationToken ct)
    {
        await _pipelineService.WaitToReadAsync(ct).ConfigureAwait(false);
    }

    private async Task ProcessOneAsync(CellCompletedRecord record, CancellationToken cancellationToken)
    {
        var label = record.CellData.DisplayLabel;
        Logger.Info($"[{record.CellData.ProcessType}] Start processing {label}");

        foreach (var consumer in _consumers)
        {
            try
            {
                var success = await DataPipelineConsumerCall
                    .ExecuteAsync(
                        ct => consumer.ProcessAsync(record, ct),
                        _consumerCallTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!success)
                {
                    await HandleFailureAsync(record, consumer, "Consumer returned false.").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(record, consumer, ResolveFailureMessage(ex)).ConfigureAwait(false);
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

        if (consumer.RetryChannel == DataPipelineRetryChannel.None)
        {
            var details =
                $"[{record.CellData.ProcessType}] Durable consumer {consumer.Name} failed for {label}, but RetryChannel is not configured.";
            Logger.Error(details);
            _criticalFallbackWriter.Write("DataPipeline.ProcessQueue.InvalidRetryChannel", details);
            return;
        }

        Logger.Warn(
            $"[{record.CellData.ProcessType}] {consumer.Name} failed for {label}. Move to retry channel {consumer.RetryChannel}.");

        switch (consumer.RetryChannel)
        {
            case DataPipelineRetryChannel.Cloud:
                await PersistCloudFailureAsync(record, consumer.Name, errorMessage).ConfigureAwait(false);
                return;
            case DataPipelineRetryChannel.Mes:
                await PersistMesFailureAsync(record, consumer.Name, errorMessage).ConfigureAwait(false);
                return;
            case DataPipelineRetryChannel.None:
                return;
        }

        var unsupportedDetails =
            $"[{record.CellData.ProcessType}] Unsupported retry channel {consumer.RetryChannel} for {consumer.Name}.";
        Logger.Error(unsupportedDetails);
        _criticalFallbackWriter.Write("DataPipeline.ProcessQueue.UnsupportedRetryChannel", unsupportedDetails);
    }

    private static string ResolveFailureMessage(Exception ex)
        => ex is TimeoutException ? "timeout_exceeded" : ex.Message;

    private async Task PersistCloudFailureAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage)
    {
        // Cloud 链路失败只写 Cloud retry/fallback/deadletter。补偿表里保存完整 CellDataJson，
        // 不拆插件字段，后续 CloudRetryTask 反序列化后再回到对应 uploader。
        var label = record.CellData.DisplayLabel;
        var retryBlockedReason = await _capacityGuard
            .GetCloudRetryBlockReasonAsync(record.CellData.ProcessType)
            .ConfigureAwait(false);

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

            var fallbackBlockedReason = await _capacityGuard
                .GetCloudFallbackBlockReasonAsync(record.CellData.ProcessType)
                .ConfigureAwait(false);

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
        // MES 链路失败只写 MES retry/fallback/deadletter。这里不调用 MES 接口，
        // 也不把数据转交 Cloud；MesRetryTask 会在 MES 心跳恢复后按 CellDataJson 补传。
        var label = record.CellData.DisplayLabel;
        var retryBlockedReason = await _capacityGuard
            .GetMesRetryBlockReasonAsync(record.CellData.ProcessType)
            .ConfigureAwait(false);

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
            // 首选写入 pipeline_mes.failed_mes_records，作为正常 MES 补传队列。
            await _mesRetryStore.SaveAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error($"[{record.CellData.ProcessType}] Save retry record failed for {label}: {ex.Message}");

            var fallbackBlockedReason = await _capacityGuard
                .GetMesFallbackBlockReasonAsync(record.CellData.ProcessType)
                .ConfigureAwait(false);

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
                // retry 主表不可用时写入 pipeline_mes.mes_fallback_records，等待 MesRetryTask 恢复回 retry。
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
