using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Shared;
namespace IIoT.Edge.Host.DataPipeline.Services;

/// <summary>
/// DataPipeline 失败数据的级联持久化入口，统一 retry、fallback、deadletter、critical fallback 的顺序。
/// </summary>
public sealed class DataPipelineCascadingPersistenceWriter
{
    private readonly ICloudRetryRecordStore _cloudRetryStore;
    private readonly IMesRetryRecordStore _mesRetryStore;
    private readonly ICloudFallbackBufferStore _cloudFallbackStore;
    private readonly IMesFallbackBufferStore _mesFallbackStore;
    private readonly ICloudDeadLetterStore _cloudDeadLetterStore;
    private readonly IMesDeadLetterStore _mesDeadLetterStore;
    private readonly ICriticalPersistenceFallbackWriter _criticalFallbackWriter;
    private readonly DataPipelineCapacityGuard _capacityGuard;
    private readonly ILogService _logger;
    private readonly ICellDataJsonSerializer _cellDataJsonSerializer;

    public DataPipelineCascadingPersistenceWriter(
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        ICloudFallbackBufferStore cloudFallbackStore,
        IMesFallbackBufferStore mesFallbackStore,
        ICloudDeadLetterStore cloudDeadLetterStore,
        IMesDeadLetterStore mesDeadLetterStore,
        ICriticalPersistenceFallbackWriter criticalFallbackWriter,
        DataPipelineCapacityGuard capacityGuard,
        ILogService logger,
        ICellDataJsonSerializer cellDataJsonSerializer)
    {
        _cloudRetryStore = cloudRetryStore;
        _mesRetryStore = mesRetryStore;
        _cloudFallbackStore = cloudFallbackStore;
        _mesFallbackStore = mesFallbackStore;
        _cloudDeadLetterStore = cloudDeadLetterStore;
        _mesDeadLetterStore = mesDeadLetterStore;
        _criticalFallbackWriter = criticalFallbackWriter;
        _capacityGuard = capacityGuard;
        _logger = logger;
        _cellDataJsonSerializer = cellDataJsonSerializer;
    }

    public Task<bool> PersistAsync(
        CellCompletedRecord record,
        DataPipelineRetryChannel channel,
        string failedTarget,
        string errorMessage,
        string sourceTable,
        DeadLetterStage fallbackFailureStage,
        long? sourceRecordId = null)
    {
        var operations = Resolve(channel);
        return PersistCoreAsync(
            record,
            failedTarget,
            errorMessage,
            sourceTable,
            sourceRecordId,
            fallbackFailureStage,
            operations);
    }

    private async Task<bool> PersistCoreAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage,
        string sourceTable,
        long? sourceRecordId,
        DeadLetterStage fallbackFailureStage,
        ChannelOperations operations)
    {
        var retryBlockedReason = await operations.GetRetryBlockReasonAsync(record.CellData.ProcessType).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(retryBlockedReason))
        {
            return await TryPersistDeadLetterAsync(
                record,
                failedTarget,
                sourceTable,
                sourceRecordId,
                operations,
                DeadLetterStage.CapacityBlocked,
                BuildCapacityBlockedFailureReason(CapacityBlockedChannel.Retry, retryBlockedReason),
                exception: null).ConfigureAwait(false);
        }

        try
        {
            await operations.SaveRetryAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
            _logger.Error($"[{operations.LogPrefix}] {record.CellData.DisplayLabel} 已写入 {operations.DisplayName} 补传队列。");
            return true;
        }
        catch (Exception retryEx)
        {
            _logger.Error($"[{operations.LogPrefix}] {record.CellData.DisplayLabel} 写入补传队列失败：{retryEx.Message}");

            var fallbackBlockedReason = await operations.GetFallbackBlockReasonAsync(record.CellData.ProcessType).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(fallbackBlockedReason))
            {
                return await TryPersistDeadLetterAsync(
                    record,
                    failedTarget,
                    sourceTable,
                    sourceRecordId,
                    operations,
                    DeadLetterStage.CapacityBlocked,
                    BuildCapacityBlockedFailureReason(CapacityBlockedChannel.Fallback, fallbackBlockedReason),
                    exception: null).ConfigureAwait(false);
            }

            try
            {
                await operations.SaveFallbackAsync(record, failedTarget, errorMessage).ConfigureAwait(false);
                _logger.Error($"[{operations.LogPrefix}] 补传队列不可用，{record.CellData.DisplayLabel} 已写入 {operations.DisplayName} 兜底缓存。");
                return true;
            }
            catch (Exception fallbackEx)
            {
                return await TryPersistDeadLetterAsync(
                    record,
                    failedTarget,
                    sourceTable,
                    sourceRecordId,
                    operations,
                    fallbackFailureStage,
                    $"{operations.DisplayName} 补传队列写入失败：{retryEx.Message}；兜底缓存写入失败：{fallbackEx.Message}",
                    fallbackEx).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> TryPersistDeadLetterAsync(
        CellCompletedRecord record,
        string failedTarget,
        string sourceTable,
        long? sourceRecordId,
        ChannelOperations operations,
        DeadLetterStage stage,
        string failureReason,
        Exception? exception)
    {
        try
        {
            await operations.SaveDeadLetterAsync(BuildDeadLetterRecord(
                    record,
                    failedTarget,
                    sourceTable,
                    sourceRecordId,
                    stage,
                    failureReason))
                .ConfigureAwait(false);
            _logger.Fatal($"[{operations.LogPrefix}] {record.CellData.DisplayLabel} 已进入 {operations.DisplayName} 死信。");
            return true;
        }
        catch (Exception deadLetterEx)
        {
            _criticalFallbackWriter.Write(
                operations.CriticalSource,
                $"{failureReason}；死信写入失败：{deadLetterEx.Message}",
                exception);
            return false;
        }
    }

    private ChannelOperations Resolve(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => new ChannelOperations(
                "云端",
                "云端",
                processType => _capacityGuard.GetCloudRetryBlockReasonAsync(processType),
                _cloudRetryStore.SaveAsync,
                processType => _capacityGuard.GetCloudFallbackBlockReasonAsync(processType),
                _cloudFallbackStore.SaveAsync,
                _cloudDeadLetterStore.SaveAsync,
                "DataPipeline.CloudDeadLetterPersistFailed"),
            DataPipelineRetryChannel.Mes => new ChannelOperations(
                "MES",
                "MES",
                processType => _capacityGuard.GetMesRetryBlockReasonAsync(processType),
                _mesRetryStore.SaveAsync,
                processType => _capacityGuard.GetMesFallbackBlockReasonAsync(processType),
                _mesFallbackStore.SaveAsync,
                _mesDeadLetterStore.SaveAsync,
                "DataPipeline.MesDeadLetterPersistFailed"),
            _ => throw new InvalidOperationException($"不支持的补偿链路：{channel}。")
        };

    private DeadLetterRecord BuildDeadLetterRecord(
        CellCompletedRecord record,
        string failedTarget,
        string sourceTable,
        long? sourceRecordId,
        DeadLetterStage stage,
        string failureReason)
        => new()
        {
            ProcessType = record.CellData.ProcessType,
            CellDataJson = _cellDataJsonSerializer.Serialize(record.CellData),
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
        => $"容量受限:{FormatCapacityBlockedChannel(channel)}:{blockedReason}";

    private static string FormatCapacityBlockedChannel(CapacityBlockedChannel channel)
        => channel switch
        {
            CapacityBlockedChannel.Retry => "补传",
            CapacityBlockedChannel.Fallback => "兜底",
            _ => channel.ToString()
        };

    private sealed record ChannelOperations(
        string LogPrefix,
        string DisplayName,
        Func<string, Task<string?>> GetRetryBlockReasonAsync,
        Func<CellCompletedRecord, string, string, Task> SaveRetryAsync,
        Func<string, Task<string?>> GetFallbackBlockReasonAsync,
        Func<CellCompletedRecord, string, string, Task> SaveFallbackAsync,
        Func<DeadLetterRecord, Task> SaveDeadLetterAsync,
        string CriticalSource);
}
