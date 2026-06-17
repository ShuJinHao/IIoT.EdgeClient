using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

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
                    $"{ChannelName} fallback 记录反序列化失败，工序：{fallback.ProcessType}。").ConfigureAwait(false);

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
                        $"[{DeadLetterChannelMetadata.LogPrefix}] {ChannelName} fallback 记录 {fallback.Id} 因 retry 容量阻塞继续保留，原因：{retryBlockedReason}。");
                    continue;
                }

                recoveredIds.Add(fallback.Id);
            }
            catch (Exception ex)
            {
                Logger.Error($"[{DeadLetterChannelMetadata.LogPrefix}] 恢复 {ChannelName} fallback 记录 {fallback.Id} 失败：{ex.Message}");
            }
        }

        if (deadLetterIds.Count > 0)
        {
            await FallbackStore.DeleteBatchAsync(deadLetterIds).ConfigureAwait(false);
        }

        if (recoveredIds.Count > 0)
        {
            await FallbackStore.MovePendingToRetryAsync(recoveredIds).ConfigureAwait(false);
            Logger.Info($"[{DeadLetterChannelMetadata.LogPrefix}] 已将 {recoveredIds.Count} 条 {ChannelName} fallback 记录恢复到 retry 主表。");
        }

        await RefreshFallbackCapacityStatusAsync().ConfigureAwait(false);
    }

    protected abstract Task<string?> GetRetryBlockReasonAsync(string processType);

    protected abstract Task RefreshFallbackCapacityStatusAsync();
}
