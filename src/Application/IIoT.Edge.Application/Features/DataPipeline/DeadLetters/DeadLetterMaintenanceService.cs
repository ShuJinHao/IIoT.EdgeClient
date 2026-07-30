using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Mes;

namespace IIoT.Edge.Application.Features.DataPipeline.DeadLetters;

/// <summary>
/// 死信人工处理服务。Cloud/MES 只复用操作流程，不复用存储链路。
/// </summary>
public sealed class DeadLetterMaintenanceService : IDeadLetterMaintenanceService
{
    private readonly ICloudDeadLetterStore _cloudDeadLetterStore;
    private readonly IMesDeadLetterStore _mesDeadLetterStore;
    private readonly ICloudRetryRecordStore _cloudRetryStore;
    private readonly IMesRetryRecordStore _mesRetryStore;
    private readonly ICloudDeadLetterRequeueStore _cloudRequeueStore;
    private readonly IMesDeadLetterRequeueStore _mesRequeueStore;
    private readonly ILogService _logger;

    public DeadLetterMaintenanceService(
        ICloudDeadLetterStore cloudDeadLetterStore,
        IMesDeadLetterStore mesDeadLetterStore,
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        ICloudDeadLetterRequeueStore cloudRequeueStore,
        IMesDeadLetterRequeueStore mesRequeueStore,
        ILogService logger)
    {
        _cloudDeadLetterStore = cloudDeadLetterStore;
        _mesDeadLetterStore = mesDeadLetterStore;
        _cloudRetryStore = cloudRetryStore;
        _mesRetryStore = mesRetryStore;
        _cloudRequeueStore = cloudRequeueStore;
        _mesRequeueStore = mesRequeueStore;
        _logger = logger;
    }

    public Task<IReadOnlyList<DeadLetterRecord>> GetLatestAsync(DataPipelineRetryChannel channel, int count = 50)
        => Resolve(channel).DeadLetterStore.GetLatestAsync(count);

    public Task<DeadLetterRecord?> GetByIdAsync(DataPipelineRetryChannel channel, long id)
        => Resolve(channel).DeadLetterStore.GetByIdAsync(id);

    public async Task<DeadLetterOperationResult> RequeueAsync(DataPipelineRetryChannel channel, long id)
    {
        var stores = Resolve(channel);
        var record = await stores.DeadLetterStore.GetByIdAsync(id).ConfigureAwait(false);
        if (record is null)
        {
            return DeadLetterOperationResult.Failure($"未找到{stores.DisplayName}死信记录：{id}。");
        }

        var identityBlock = GetIdentityBlockReason(record);
        if (identityBlock is not null)
        {
            return DeadLetterOperationResult.Failure(
                $"{stores.DisplayName}死信身份未解析，原记录保留：{identityBlock}");
        }

        try
        {
            await stores.SaveRetryAsync(record)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{stores.LogPrefix}] 死信 {id} 重新入队失败，原记录已保留：{ex.Message}");
            return DeadLetterOperationResult.Failure($"{stores.DisplayName}死信重新入队失败，原记录已保留：{ex.Message}");
        }

        try
        {
            await stores.DeadLetterStore.DeleteAsync(id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{stores.LogPrefix}] 死信 {id} 已写入补传队列，但删除死信记录失败，请现场确认：{ex.Message}");
            return DeadLetterOperationResult.Failure($"{stores.DisplayName}死信已写入补传队列，但删除死信记录失败，请现场确认：{ex.Message}");
        }

        _logger.Warn($"[{stores.LogPrefix}] 死信 {id} 已由人工操作重新写入补传队列。");
        return DeadLetterOperationResult.Success($"{stores.DisplayName}死信已重新写入补传队列。");
    }

    public async Task<DeadLetterOperationResult> DeleteAsync(DataPipelineRetryChannel channel, long id)
    {
        var stores = Resolve(channel);
        var record = await stores.DeadLetterStore.GetByIdAsync(id).ConfigureAwait(false);
        if (record is null)
        {
            return DeadLetterOperationResult.Failure($"未找到{stores.DisplayName}死信记录：{id}。");
        }

        var identityBlock = GetIdentityBlockReason(record);
        if (identityBlock is not null)
        {
            return DeadLetterOperationResult.Failure(
                $"{stores.DisplayName}死信身份未解析，禁止删除且原记录已保留：{identityBlock}");
        }

        try
        {
            await stores.DeadLetterStore.DeleteAsync(id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{stores.LogPrefix}] 死信 {id} 删除失败，原记录状态需现场确认：{ex.Message}");
            return DeadLetterOperationResult.Failure($"{stores.DisplayName}死信删除失败，原记录状态需现场确认：{ex.Message}");
        }

        _logger.Warn($"[{stores.LogPrefix}] 死信 {id} 已由人工操作删除。");
        return DeadLetterOperationResult.Success($"{stores.DisplayName}死信已删除。");
    }

    private DeadLetterStores Resolve(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => new DeadLetterStores(
                _cloudDeadLetterStore,
                _cloudRequeueStore.SaveRequeuedAsync,
                "Cloud",
                "云端"),
            DataPipelineRetryChannel.Mes => new DeadLetterStores(
                _mesDeadLetterStore,
                _mesRequeueStore.SaveRequeuedAsync,
                "MES",
                "MES"),
            _ => throw new InvalidOperationException($"不支持的死信通道：{channel}。")
        };

    private static string? GetIdentityBlockReason(DeadLetterRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.PlcCode))
        {
            return "PlcCode 为空";
        }

        return record.IdempotencyKeyVersion is
            CloudIdempotencyKeyVersion.LegacyV1 or CloudIdempotencyKeyVersion.PlcStableV2
            ? null
            : $"幂等版本 {record.IdempotencyKeyVersion} 无效";
    }

    private sealed record DeadLetterStores(
        IDeadLetterDiagnosticsStore DeadLetterStore,
        Func<DeadLetterRecord, Task> SaveRetryAsync,
        string LogPrefix,
        string DisplayName);
}
