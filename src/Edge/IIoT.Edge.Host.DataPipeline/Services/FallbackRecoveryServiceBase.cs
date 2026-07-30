using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

namespace IIoT.Edge.Host.DataPipeline.Services;

internal abstract class FallbackRecoveryServiceBase<TFallbackRecord> : RetryDeadLetterServiceBase
    where TFallbackRecord : IFallbackRecord
{
    protected FallbackRecoveryServiceBase(
        ILogService logger,
        IFallbackBufferStore<TFallbackRecord> fallbackStore,
        IDeadLetterStore deadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        IDataPipelineDeadLetterWriter deadLetterWriter,
        ICellDataJsonSerializer cellDataJsonSerializer,
        DataPipelineDeadLetterChannel deadLetterChannel)
        : base(
            logger,
            deadLetterStore,
            criticalFallbackWriter,
            deadLetterWriter,
            cellDataJsonSerializer,
            deadLetterChannel)
    {
        FallbackStore = fallbackStore;
    }

    protected IFallbackBufferStore<TFallbackRecord> FallbackStore { get; }

    protected abstract string ChannelName { get; }

    protected abstract string SourceTable { get; }

    public async Task RecoverAsync()
    {
        var pending = await FallbackStore.GetPendingAsync().ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }

        var recoveredIds = new List<long>();
        var deadLetterIds = new List<long>();
        foreach (var fallback in pending)
        {
            if (string.IsNullOrWhiteSpace(fallback.PlcCode)
                || fallback.IdempotencyKeyVersion is not (
                    CloudIdempotencyKeyVersion.LegacyV1
                    or CloudIdempotencyKeyVersion.PlcStableV2))
            {
                Logger.Error(
                    $"[PlcCode={FormatPlcCode(fallback.PlcCode)}][TaskKey={fallback.TaskKey}] "
                    + $"{ChannelDisplayName}兜底记录 {fallback.Id} 身份未解析，原记录保留并停止移动。");
                continue;
            }

            var cellData = DeserializeCellData(fallback.ProcessType, fallback.CellDataJson);
            if (cellData is null)
            {
                var persisted = await TryPersistDeadLetterAsync(
                    fallback.ProcessType,
                    fallback.CellDataJson,
                    fallback.FailedTarget,
                    SourceTable,
                    fallback.Id,
                    DeadLetterStage.FallbackRecoverDeserialize,
                    $"{ChannelDisplayName}兜底记录反序列化失败，工序：{fallback.ProcessType}。",
                    CreateSourceRecord(fallback)).ConfigureAwait(false);

                if (persisted)
                {
                    deadLetterIds.Add(fallback.Id);
                }

                continue;
            }

            try
            {
                var retryBlockedReason = await GetRetryBlockReasonAsync(fallback.ProcessType).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(retryBlockedReason))
                {
                    Logger.Warn(
                        $"[PlcCode={fallback.PlcCode}][{DeadLetterChannelMetadata.LogPrefix}] {ChannelDisplayName}兜底记录 {fallback.Id} 因补传容量阻塞继续保留，原因：{retryBlockedReason}。");
                    continue;
                }

                recoveredIds.Add(fallback.Id);
            }
            catch (Exception ex)
            {
                Logger.Error($"[PlcCode={fallback.PlcCode}][{DeadLetterChannelMetadata.LogPrefix}] 恢复 {ChannelDisplayName}兜底记录 {fallback.Id} 失败：{ex.Message}");
            }
        }

        if (deadLetterIds.Count > 0)
        {
            await FallbackStore.DeleteBatchAsync(deadLetterIds).ConfigureAwait(false);
        }

        if (recoveredIds.Count > 0)
        {
            await FallbackStore.MovePendingToRetryAsync(recoveredIds).ConfigureAwait(false);
            Logger.Info($"[{DeadLetterChannelMetadata.LogPrefix}] 已将 {recoveredIds.Count} 条 {ChannelDisplayName}兜底记录恢复到补传主表。");
        }

        await RefreshFallbackCapacityStatusAsync().ConfigureAwait(false);
    }

    protected abstract Task<string?> GetRetryBlockReasonAsync(string processType);

    protected abstract Task RefreshFallbackCapacityStatusAsync();

    private string ChannelDisplayName
        => ChannelName switch
        {
            "Cloud" => "云端",
            "MES" => "MES",
            _ => ChannelName
        };

    private static FailedCellRecord CreateSourceRecord(TFallbackRecord fallback)
        => new()
        {
            Id = fallback.Id,
            ProcessType = fallback.ProcessType,
            CellDataJson = fallback.CellDataJson,
            FailedTarget = fallback.FailedTarget,
            PlcCode = fallback.PlcCode,
            NetworkDeviceId = fallback.NetworkDeviceId,
            DeviceName = fallback.DeviceName,
            ModuleId = fallback.ModuleId,
            TaskKey = fallback.TaskKey,
            PlanSessionId = fallback.PlanSessionId,
            MainPlanCode = fallback.MainPlanCode,
            TraceBatchNumber = fallback.TraceBatchNumber,
            IdempotencyKeyVersion = fallback.IdempotencyKeyVersion
        };

    private static string FormatPlcCode(string? plcCode)
        => string.IsNullOrWhiteSpace(plcCode) ? "未解析" : plcCode;
}
