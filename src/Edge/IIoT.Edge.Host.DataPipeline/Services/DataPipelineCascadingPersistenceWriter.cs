using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using System.Text.Json;

using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Shared;
namespace IIoT.Edge.Host.DataPipeline.Services;

/// <summary>
/// DataPipeline 失败数据的级联持久化入口，统一 retry、fallback、deadletter、critical fallback 的顺序。
/// </summary>
public sealed class DataPipelineCascadingPersistenceWriter
{
    private static readonly JsonSerializerOptions CriticalRecoveryJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
        long? sourceRecordId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operations = Resolve(channel);
        return PersistCoreAsync(
            record,
            failedTarget,
            errorMessage,
            sourceTable,
            sourceRecordId,
            fallbackFailureStage,
            operations,
            cancellationToken);
    }

    public Task<bool> PersistNonRetryableAsync(
        CellCompletedRecord record,
        DataPipelineRetryChannel channel,
        string failedTarget,
        string reasonCode,
        string sourceTable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operations = Resolve(channel);
        return TryPersistDeadLetterAsync(
            record,
            failedTarget,
            sourceTable,
            sourceRecordId: null,
            operations,
            DeadLetterStage.InvalidPayload,
            reasonCode,
            exception: null,
            cancellationToken);
    }

    public void WriteDurableShutdownCriticalEvidence(
        CellCompletedRecord record,
        DataPipelineRetryChannel channel,
        string failedTarget,
        string failureReason,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(failedTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        ArgumentNullException.ThrowIfNull(exception);

        var operations = Resolve(channel);
        var sourceTable = DataPipelineRetryChannelMetadata.TryGetFailedRecordSourceTable(channel);
        if (string.IsNullOrWhiteSpace(sourceTable))
        {
            throw new InvalidOperationException(
                $"无法为 {DataPipelineRetryChannelMetadata.Format(channel)} 生成 shutdown 恢复证据。");
        }

        WriteCriticalRecoveryEvidence(
            record,
            operations,
            failedTarget,
            sourceTable,
            sourceRecordId: null,
            DeadLetterStage.DurableShutdownPersist,
            failureReason,
            exception,
            $"DataPipeline.ProcessQueue.{FormatRecoveryChannel(channel)}.ShutdownPersistenceFailed");
    }

    private async Task<bool> PersistCoreAsync(
        CellCompletedRecord record,
        string failedTarget,
        string errorMessage,
        string sourceTable,
        long? sourceRecordId,
        DeadLetterStage fallbackFailureStage,
        ChannelOperations operations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Exception? retryFailure = null;
        string? retryBlockedReason = null;
        try
        {
            retryBlockedReason = await operations
                .GetRetryBlockReasonAsync(record.CellData.ProcessType, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            retryFailure = ex;
            WriteLogBestEffort(logger =>
                logger.Error(
                    $"{DataPipelineLogContext.Format(record)}[{operations.LogPrefix}] " +
                    $"阶段=RetryCapacityCheck，结果=Failed，异常类型={ex.GetType().Name}。"));
        }

        if (retryFailure is null)
        {
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
                    exception: null,
                    cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await operations.SaveRetryAsync(record, failedTarget, errorMessage, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                retryFailure = ex;
                WriteLogBestEffort(logger =>
                    logger.Error(
                        $"{DataPipelineLogContext.Format(record)}[{operations.LogPrefix}] " +
                        $"阶段=RetryPersist，结果=Failed，异常类型={ex.GetType().Name}。"));
            }

            if (retryFailure is null)
            {
                WriteLogBestEffort(logger =>
                    logger.Info(
                        $"{DataPipelineLogContext.Format(record)}[{operations.LogPrefix}] " +
                        $"结果=DurableRetryHandoff，说明=已本地落盘，尚未上传成功。"));
                return true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        string? fallbackBlockedReason;
        try
        {
            fallbackBlockedReason = await operations
                .GetFallbackBlockReasonAsync(record.CellData.ProcessType, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception fallbackGuardEx)
        {
            WriteLogBestEffort(logger =>
                logger.Error(
                    $"{DataPipelineLogContext.Format(record)}[{operations.LogPrefix}] " +
                    $"阶段=FallbackCapacityCheck，结果=Failed，" +
                    $"异常类型={fallbackGuardEx.GetType().Name}。"));
            return await TryPersistDeadLetterAsync(
                record,
                failedTarget,
                sourceTable,
                sourceRecordId,
                operations,
                fallbackFailureStage,
                $"{operations.DisplayName} 补传链路失败：{retryFailure!.Message}；兜底容量检查失败：{fallbackGuardEx.Message}",
                fallbackGuardEx,
                cancellationToken).ConfigureAwait(false);
        }

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
                exception: retryFailure,
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await operations.SaveFallbackAsync(record, failedTarget, errorMessage, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
                $"{operations.DisplayName} 补传链路失败：{retryFailure!.Message}；兜底缓存写入失败：{fallbackEx.Message}",
                fallbackEx,
                cancellationToken).ConfigureAwait(false);
        }

        WriteLogBestEffort(logger =>
            logger.Warn(
                $"{DataPipelineLogContext.Format(record)}[{operations.LogPrefix}] " +
                $"结果=DurableFallbackHandoff，说明=已本地落盘，尚未上传成功。"));
        return true;
    }

    private async Task<bool> TryPersistDeadLetterAsync(
        CellCompletedRecord record,
        string failedTarget,
        string sourceTable,
        long? sourceRecordId,
        ChannelOperations operations,
        DeadLetterStage stage,
        string failureReason,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await operations.SaveDeadLetterAsync(BuildDeadLetterRecord(
                    record,
                    failedTarget,
                    sourceTable,
                    sourceRecordId,
                    stage,
                    failureReason),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception deadLetterEx)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCriticalRecoveryEvidence(
                record,
                operations,
                failedTarget,
                sourceTable,
                sourceRecordId,
                stage,
                $"{failureReason}；死信写入失败：{deadLetterEx.Message}",
                deadLetterEx,
                operations.CriticalSource);
            return false;
        }

        WriteLogBestEffort(logger =>
            logger.Fatal(
                $"{DataPipelineLogContext.Format(record)}[{operations.LogPrefix}] " +
                $"结果=DurableDeadLetter，阶段={stage}，尚未上传成功。"));
        return true;
    }

    private void WriteLogBestEffort(Action<ILogService> writeLog)
    {
        try
        {
            writeLog(_logger);
        }
        catch (Exception)
        {
            // 日志订阅者失败不能改变任一级已提交的恢复状态或中断恢复链。
        }
    }

    private void WriteCriticalRecoveryEvidence(
        CellCompletedRecord record,
        ChannelOperations operations,
        string failedTarget,
        string sourceTable,
        long? sourceRecordId,
        DeadLetterStage stage,
        string failureReason,
        Exception exception,
        string criticalSource)
    {
        var details = JsonSerializer.Serialize(
            new CriticalRecoveryEnvelope(
                SchemaVersion: 1,
                Channel: FormatRecoveryChannel(operations.Channel),
                FailedTarget: failedTarget,
                SourceTable: sourceTable,
                SourceRecordId: sourceRecordId,
                FailureStage: stage.ToString(),
                FailureReason: failureReason,
                ProcessType: record.CellData.ProcessType,
                CellDataJson: _cellDataJsonSerializer.Serialize(record.CellData),
                PlcCode: record.ResolvePlcCode(),
                IdempotencyKeyVersion: record.IdempotencyKeyVersion,
                NetworkDeviceId: record.ResolveNetworkDeviceId(),
                DeviceName: record.ResolveDeviceName(),
                ModuleId: record.ModuleId,
                TaskKey: record.TaskKey,
                PlanSessionId: record.PlanSessionId,
                MainPlanCode: record.MainPlanCode,
                TraceBatchNumber: record.TraceBatchNumber,
                CreatedAtUtc: DateTime.UtcNow),
            CriticalRecoveryJsonOptions);

        _criticalFallbackWriter.Write(criticalSource, details, exception);
    }

    private ChannelOperations Resolve(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => new ChannelOperations(
                DataPipelineRetryChannel.Cloud,
                DataPipelineRetryChannelMetadata.Format(channel),
                DataPipelineRetryChannelMetadata.Format(channel),
                (processType, cancellationToken) => _capacityGuard.GetCloudRetryBlockReasonAsync(processType).WaitAsync(cancellationToken),
                _cloudRetryStore.SaveAsync,
                (processType, cancellationToken) => _capacityGuard.GetCloudFallbackBlockReasonAsync(processType).WaitAsync(cancellationToken),
                _cloudFallbackStore.SaveAsync,
                _cloudDeadLetterStore.SaveAsync,
                "DataPipeline.CloudDeadLetterPersistFailed"),
            DataPipelineRetryChannel.Mes => new ChannelOperations(
                DataPipelineRetryChannel.Mes,
                DataPipelineRetryChannelMetadata.Format(channel),
                DataPipelineRetryChannelMetadata.Format(channel),
                (processType, cancellationToken) => _capacityGuard.GetMesRetryBlockReasonAsync(processType).WaitAsync(cancellationToken),
                _mesRetryStore.SaveAsync,
                (processType, cancellationToken) => _capacityGuard.GetMesFallbackBlockReasonAsync(processType).WaitAsync(cancellationToken),
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
            CreatedAt = DateTime.UtcNow,
            PlcCode = record.ResolvePlcCode(),
            IdempotencyKeyVersion = record.IdempotencyKeyVersion,
            NetworkDeviceId = record.ResolveNetworkDeviceId(),
            DeviceName = record.ResolveDeviceName(),
            ModuleId = record.ModuleId,
            TaskKey = record.TaskKey,
            PlanSessionId = record.PlanSessionId,
            MainPlanCode = record.MainPlanCode,
            TraceBatchNumber = record.TraceBatchNumber
        };

    private static string BuildCapacityBlockedFailureReason(
        CapacityBlockedChannel channel,
        string blockedReason)
        => $"容量受限:{FormatCapacityBlockedChannel(channel)}:{blockedReason}";

    private static string FormatRecoveryChannel(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => "Cloud",
            DataPipelineRetryChannel.Mes => "MES",
            _ => throw new InvalidOperationException($"不支持的恢复证据通道：{channel}。")
        };

    private static string FormatCapacityBlockedChannel(CapacityBlockedChannel channel)
        => channel switch
        {
            CapacityBlockedChannel.Retry => "补传",
            CapacityBlockedChannel.Fallback => "兜底",
            _ => channel.ToString()
        };

    private sealed record ChannelOperations(
        DataPipelineRetryChannel Channel,
        string LogPrefix,
        string DisplayName,
        Func<string, CancellationToken, Task<string?>> GetRetryBlockReasonAsync,
        Func<CellCompletedRecord, string, string, CancellationToken, Task> SaveRetryAsync,
        Func<string, CancellationToken, Task<string?>> GetFallbackBlockReasonAsync,
        Func<CellCompletedRecord, string, string, CancellationToken, Task> SaveFallbackAsync,
        Func<DeadLetterRecord, CancellationToken, Task> SaveDeadLetterAsync,
        string CriticalSource);

    private sealed record CriticalRecoveryEnvelope(
        int SchemaVersion,
        string Channel,
        string FailedTarget,
        string SourceTable,
        long? SourceRecordId,
        string FailureStage,
        string FailureReason,
        string ProcessType,
        string CellDataJson,
        string PlcCode,
        CloudIdempotencyKeyVersion IdempotencyKeyVersion,
        int? NetworkDeviceId,
        string DeviceName,
        string? ModuleId,
        string? TaskKey,
        string? PlanSessionId,
        string? MainPlanCode,
        string? TraceBatchNumber,
        DateTime CreatedAtUtc);
}
