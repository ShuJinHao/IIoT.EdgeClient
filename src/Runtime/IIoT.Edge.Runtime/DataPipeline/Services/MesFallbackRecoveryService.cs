using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

public interface IMesFallbackRecoveryService
{
    Task RecoverAsync();
}

internal sealed class MesFallbackRecoveryService : IMesFallbackRecoveryService
{
    private static readonly DataPipelineDeadLetterChannel DeadLetterChannel = new(
        LogPrefix: "Retry-MES",
        DeadLetterName: "MES",
        CriticalSource: "Retry.MesDeadLetterPersistFailed");

    private readonly ILogService _logger;
    private readonly IMesFallbackBufferStore _fallbackStore;
    private readonly IMesDeadLetterStore _deadLetterStore;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly DataPipelineCapacityGuard _capacityGuard;
    private readonly IDataPipelineDeadLetterWriter _deadLetterWriter;
    private readonly ICellDataJsonSerializer _cellDataJsonSerializer;

    public MesFallbackRecoveryService(
        ILogService logger,
        IMesFallbackBufferStore fallbackStore,
        IMesDeadLetterStore deadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        DataPipelineCapacityGuard capacityGuard,
        IDataPipelineDeadLetterWriter deadLetterWriter,
        ICellDataJsonSerializer cellDataJsonSerializer)
    {
        _logger = logger;
        _fallbackStore = fallbackStore;
        _deadLetterStore = deadLetterStore;
        _criticalFallbackWriter = criticalFallbackWriter;
        _capacityGuard = capacityGuard;
        _deadLetterWriter = deadLetterWriter;
        _cellDataJsonSerializer = cellDataJsonSerializer;
    }

    public async Task RecoverAsync()
    {
        var pending = await _fallbackStore.GetPendingAsync().ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }

        var recoveredIds = new List<long>();
        var deadLetterIds = new List<long>();
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
                    $"MES fallback 记录反序列化失败，工序：{fallback.ProcessType}。").ConfigureAwait(false);

                if (persisted)
                {
                    deadLetterIds.Add(fallback.Id);
                }

                continue;
            }

            try
            {
                var retryBlockedReason = await _capacityGuard
                    .GetMesRetryBlockReasonAsync(fallback.ProcessType)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(retryBlockedReason))
                {
                    _logger.Warn(
                        $"[Retry-MES] MES fallback 记录 {fallback.Id} 因 retry 容量阻塞继续保留，原因：{retryBlockedReason}。");
                    continue;
                }

                recoveredIds.Add(fallback.Id);
            }
            catch (Exception ex)
            {
                _logger.Error($"[Retry-MES] 恢复 MES fallback 记录 {fallback.Id} 失败：{ex.Message}");
            }
        }

        if (deadLetterIds.Count > 0)
        {
            await _fallbackStore.DeleteBatchAsync(deadLetterIds).ConfigureAwait(false);
        }

        if (recoveredIds.Count > 0)
        {
            await _fallbackStore.MovePendingToRetryAsync(recoveredIds).ConfigureAwait(false);
            _logger.Info($"[Retry-MES] 已将 {recoveredIds.Count} 条 MES fallback 记录恢复到 retry 主表。");
        }

        await _capacityGuard.RefreshMesFallbackCapacityStatusAsync().ConfigureAwait(false);
    }

    private CellDataBase? DeserializeCellData(string processType, string json)
    {
        try
        {
            return _cellDataJsonSerializer.Deserialize(processType, json);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Retry-MES] CellData 反序列化失败：{ex.Message}");
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
        => await _deadLetterWriter.TryPersistAsync(
            _deadLetterStore.SaveAsync,
            _criticalFallbackWriter,
            _logger,
            DeadLetterChannel,
            processType,
            cellDataJson,
            failedTarget,
            sourceTable,
            sourceRecordId,
            stage,
            failureReason).ConfigureAwait(false);
}
