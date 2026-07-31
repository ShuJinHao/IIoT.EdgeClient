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

        var recoveredRecords = new List<(TFallbackRecord Record, CellDataBase CellData)>();
        var deadLetterIds = new List<long>();
        foreach (var fallback in pending)
        {
            if (string.IsNullOrWhiteSpace(fallback.PlcCode)
                || fallback.IdempotencyKeyVersion is not (
                    CloudIdempotencyKeyVersion.LegacyV1
                    or CloudIdempotencyKeyVersion.PlcStableV2))
            {
                Logger.Error(
                    $"{DataPipelineLogContext.FormatFallback(fallback)}" +
                    $"[{DeadLetterChannelMetadata.LogPrefix}] 结果=Blocked，" +
                    "原因码=IdentityUnresolved，原记录保留。");
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
                        $"{DataPipelineLogContext.FormatFallback(fallback, cellData)}" +
                        $"[{DeadLetterChannelMetadata.LogPrefix}] 结果=Blocked，" +
                        $"原因码={retryBlockedReason}，兜底记录保留。");
                    continue;
                }

                recoveredRecords.Add((fallback, cellData));
            }
            catch (Exception ex)
            {
                Logger.Error(
                    $"{DataPipelineLogContext.FormatFallback(fallback, cellData)}" +
                    $"[{DeadLetterChannelMetadata.LogPrefix}] 结果=RecoveryFailed，" +
                    $"异常类型={ex.GetType().Name}，原记录保留。");
            }
        }

        if (deadLetterIds.Count > 0)
        {
            await FallbackStore.DeleteBatchAsync(deadLetterIds).ConfigureAwait(false);
        }

        if (recoveredRecords.Count > 0)
        {
            await FallbackStore
                .MovePendingToRetryAsync(recoveredRecords.Select(item => item.Record.Id).ToArray())
                .ConfigureAwait(false);
            foreach (var (record, cellData) in recoveredRecords)
            {
                Logger.Info(
                    $"{DataPipelineLogContext.FormatFallback(record, cellData)}" +
                    $"[{DeadLetterChannelMetadata.LogPrefix}] 结果=DurableRetryHandoff，" +
                    "说明=已恢复到本地补传表，尚未上传成功。");
            }
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

}
