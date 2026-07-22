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
    private readonly ILogService _logger;

    public DeadLetterMaintenanceService(
        ICloudDeadLetterStore cloudDeadLetterStore,
        IMesDeadLetterStore mesDeadLetterStore,
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        ILogService logger)
    {
        _cloudDeadLetterStore = cloudDeadLetterStore;
        _mesDeadLetterStore = mesDeadLetterStore;
        _cloudRetryStore = cloudRetryStore;
        _mesRetryStore = mesRetryStore;
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

        try
        {
            await stores.SaveRetryAsync(
                    record.ProcessType,
                    record.CellDataJson,
                    record.FailedTarget,
                    BuildRequeueReason(record))
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
                _cloudRetryStore.SaveRawAsync,
                "Cloud",
                "云端"),
            DataPipelineRetryChannel.Mes => new DeadLetterStores(
                _mesDeadLetterStore,
                _mesRetryStore.SaveRawAsync,
                "MES",
                "MES"),
            _ => throw new InvalidOperationException($"不支持的死信通道：{channel}。")
        };

    private static string BuildRequeueReason(DeadLetterRecord record)
        => $"manual_requeue:{record.FailureStage}:{record.FailureReason}";

    private sealed record DeadLetterStores(
        IDeadLetterDiagnosticsStore DeadLetterStore,
        Func<string, string, string, string, Task> SaveRetryAsync,
        string LogPrefix,
        string DisplayName);
}
