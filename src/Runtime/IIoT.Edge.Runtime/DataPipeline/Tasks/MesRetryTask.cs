using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Runtime.DataPipeline.Tasks;

public sealed class MesRetryTask : ScheduledTaskBase
{
    private readonly IMesRetryRecordStore _retryStore;
    private readonly IMesFallbackBufferStore _fallbackStore;
    private readonly IMesDeadLetterStore _deadLetterStore;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly IMesConsumer _mesConsumer;
    private readonly IMesRetryDiagnosticsStore _diagnosticsStore;
    private readonly DataPipelineCapacityGuard? _capacityGuard;

    private const int MaxRetryCount = 20;
    private static readonly DateTime AbandonedRetryTimeUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

    public override string TaskName => "MesRetryTask";
    protected override int ExecuteInterval => 5000;

    public MesRetryTask(
        ILogService logger,
        IMesRetryRecordStore retryStore,
        IMesFallbackBufferStore fallbackStore,
        IMesDeadLetterStore deadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        IMesConsumer mesConsumer,
        IMesRetryDiagnosticsStore diagnosticsStore,
        DataPipelineCapacityGuard? capacityGuard = null)
        : base(logger)
    {
        _retryStore = retryStore;
        _fallbackStore = fallbackStore;
        _deadLetterStore = deadLetterStore;
        _criticalFallbackWriter = criticalFallbackWriter;
        _mesConsumer = mesConsumer;
        _diagnosticsStore = diagnosticsStore;
        _capacityGuard = capacityGuard;
    }

    internal Task ExecuteOneIterationAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ExecuteAsync().WaitAsync(ct);
    }

    protected override async Task ExecuteAsync()
    {
        await RecoverFallbackRecordsAsync().ConfigureAwait(false);

        var records = await _retryStore.GetPendingAsync(batchSize: 5).ConfigureAwait(false);
        if (records.Count == 0)
        {
            if (_capacityGuard is not null)
            {
                await _capacityGuard.RefreshMesRetryCapacityStatusAsync().ConfigureAwait(false);
                await _capacityGuard.RefreshMesFallbackCapacityStatusAsync().ConfigureAwait(false);
            }

            await ApplyIdleOrBackoffStateAsync().ConfigureAwait(false);
            return;
        }

        _diagnosticsStore.SetRuntimeState(MesRetryRuntimeState.Retrying);
        var hadFailure = false;
        foreach (var record in records)
        {
            if (!await ProcessOneAsync(record).ConfigureAwait(false))
            {
                hadFailure = true;
            }
        }

        if (hadFailure)
        {
            if (_capacityGuard is not null)
            {
                await _capacityGuard.RefreshMesRetryCapacityStatusAsync().ConfigureAwait(false);
                await _capacityGuard.RefreshMesFallbackCapacityStatusAsync().ConfigureAwait(false);
            }

            _diagnosticsStore.SetRuntimeState(MesRetryRuntimeState.LastFailed);
            return;
        }

        if (_capacityGuard is not null)
        {
            await _capacityGuard.RefreshMesRetryCapacityStatusAsync().ConfigureAwait(false);
            await _capacityGuard.RefreshMesFallbackCapacityStatusAsync().ConfigureAwait(false);
        }

        await ApplyIdleOrBackoffStateAsync().ConfigureAwait(false);
    }

    private async Task RecoverFallbackRecordsAsync()
    {
        var pending = await _fallbackStore.GetPendingAsync().ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }

        var recoveredIds = new List<long>();
        foreach (var fallback in pending)
        {
            var cellData = DeserializeCellData(fallback.ProcessType, fallback.CellDataJson);
            if (cellData is null)
            {
                var persisted = await TryPersistDeadLetterAsync(
                    fallback.ProcessType,
                    fallback.CellDataJson,
                    fallback.FailedTarget,
                    sourceTable: "mes_fallback_records",
                    sourceRecordId: fallback.Id,
                    DeadLetterStage.FallbackRecoverDeserialize,
                    $"MES fallback deserialize failed for process type {fallback.ProcessType}.").ConfigureAwait(false);

                if (persisted)
                {
                    recoveredIds.Add(fallback.Id);
                }

                continue;
            }

            try
            {
                var retryBlockedReason = _capacityGuard is null
                    ? null
                    : await _capacityGuard.GetMesRetryBlockReasonAsync(fallback.ProcessType).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(retryBlockedReason))
                {
                    Logger.Warn(
                        $"[Retry-MES] MES fallback record {fallback.Id} remains buffered because retry capacity is blocked by {retryBlockedReason}.");
                    continue;
                }

                await _retryStore.SaveAsync(
                    new CellCompletedRecord { CellData = cellData },
                    fallback.FailedTarget,
                    fallback.ErrorMessage).ConfigureAwait(false);
                recoveredIds.Add(fallback.Id);
            }
            catch (Exception ex)
            {
                Logger.Error($"[Retry-MES] Failed to rehydrate MES fallback record {fallback.Id}: {ex.Message}");
            }
        }

        if (recoveredIds.Count > 0)
        {
            await _fallbackStore.DeleteBatchAsync(recoveredIds).ConfigureAwait(false);
            Logger.Info($"[Retry-MES] Recovered {recoveredIds.Count} MES fallback record(s) into the main retry store.");
        }

        if (_capacityGuard is not null)
        {
            await _capacityGuard.RefreshMesFallbackCapacityStatusAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> ProcessOneAsync(FailedCellRecord record)
    {
        var cellData = DeserializeCellData(record.ProcessType, record.CellDataJson);
        if (cellData is null)
        {
            var persisted = await TryPersistDeadLetterAsync(
                record.ProcessType,
                record.CellDataJson,
                record.FailedTarget,
                sourceTable: "failed_mes_records",
                sourceRecordId: record.Id,
                DeadLetterStage.RetryDeserialize,
                $"MES retry deserialize failed for process type {record.ProcessType}.").ConfigureAwait(false);

            if (persisted)
            {
                await _retryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            }

            return true;
        }

        var completedRecord = new CellCompletedRecord { CellData = cellData };
        var success = await _mesConsumer.ProcessAsync(completedRecord).ConfigureAwait(false);
        if (success)
        {
            await _retryStore.DeleteAsync(record.Id).ConfigureAwait(false);
            Logger.Info($"[Retry-MES] {cellData.DisplayLabel} retry succeeded and the record was removed.");
            return true;
        }

        await HandleRetryFailureAsync(record, "Consumer returned false.").ConfigureAwait(false);
        return false;
    }

    private async Task HandleRetryFailureAsync(FailedCellRecord record, string errorMessage)
    {
        var newRetryCount = record.RetryCount + 1;

        if (newRetryCount > MaxRetryCount)
        {
            Logger.Warn($"[Retry-MES] {record.ProcessType} reached max retry count {MaxRetryCount}. Auto retry stopped.");
            await _retryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, AbandonedRetryTimeUtc).ConfigureAwait(false);
            return;
        }

        var nextRetryTime = DateTime.UtcNow.Add(CalculateBackoff(newRetryCount));
        await _retryStore.UpdateRetryAsync(record.Id, newRetryCount, errorMessage, nextRetryTime).ConfigureAwait(false);
    }

    private async Task ApplyIdleOrBackoffStateAsync()
    {
        var pendingCount = await _retryStore.GetCountAsync().ConfigureAwait(false);
        _diagnosticsStore.SetRuntimeState(
            pendingCount > 0
                ? MesRetryRuntimeState.Backoff
                : MesRetryRuntimeState.Idle);
    }

    private static TimeSpan CalculateBackoff(int retryCount)
    {
        if (retryCount <= 5)
        {
            return TimeSpan.FromSeconds(30);
        }

        if (retryCount <= 10)
        {
            return TimeSpan.FromMinutes(5);
        }

        return TimeSpan.FromMinutes(30);
    }

    private CellDataBase? DeserializeCellData(string processType, string json)
    {
        try
        {
            return CellDataJsonSerializer.Deserialize(processType, json);
        }
        catch (Exception ex)
        {
            Logger.Error($"[Retry-MES] CellData deserialize failed: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> TryPersistDeadLetterAsync(
        string processType,
        string cellDataJson,
        string failedTarget,
        string sourceTable,
        long sourceRecordId,
        DeadLetterStage stage,
        string failureReason)
    {
        try
        {
            await _deadLetterStore.SaveAsync(new DeadLetterRecord
            {
                ProcessType = processType,
                CellDataJson = cellDataJson,
                FailedTarget = failedTarget,
                SourceTable = sourceTable,
                SourceRecordId = sourceRecordId,
                FailureStage = stage.ToString(),
                FailureReason = failureReason,
                CreatedAt = DateTime.UtcNow
            }).ConfigureAwait(false);

            Logger.Fatal($"[Retry-MES] {processType} record {sourceRecordId} moved into MES dead-letter store.");
            return true;
        }
        catch (Exception ex)
        {
            _criticalFallbackWriter.Write(
                "Retry.MesDeadLetterPersistFailed",
                $"{failureReason} Dead-letter save failed: {ex.Message}",
                ex);
            return false;
        }
    }
}
