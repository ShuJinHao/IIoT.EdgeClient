using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

public interface IMesRetryHousekeepingService
{
    Task RecoverAbandonedRecordsAsync();

    Task CleanupExpiredAbandonedRecordsAsync();

    Task ApplyIdleOrBackoffStateAsync();
}

internal sealed class MesRetryHousekeepingService : IMesRetryHousekeepingService
{
    private static readonly TimeSpan AbandonedRetention = TimeSpan.FromDays(30);

    private readonly ILogService _logger;
    private readonly IMesRetryRecordStore _retryStore;
    private readonly IMesRetryDiagnosticsStore _diagnosticsStore;
    private DateOnly? _lastAbandonedCleanupDateUtc;

    public MesRetryHousekeepingService(
        ILogService logger,
        IMesRetryRecordStore retryStore,
        IMesRetryDiagnosticsStore diagnosticsStore)
    {
        _logger = logger;
        _retryStore = retryStore;
        _diagnosticsStore = diagnosticsStore;
    }

    public async Task RecoverAbandonedRecordsAsync()
    {
        try
        {
            await _retryStore.ResetAllAbandonedAsync().ConfigureAwait(false);
            _logger.Info("[Retry-MES] MES 心跳已恢复，弃置记录已重置为可补传。");
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Retry-MES] 重置弃置记录失败：{ex.Message}");
        }
    }

    public async Task CleanupExpiredAbandonedRecordsAsync()
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_lastAbandonedCleanupDateUtc == todayUtc)
        {
            return;
        }

        _lastAbandonedCleanupDateUtc = todayUtc;

        try
        {
            var deleted = await _retryStore
                .DeleteExpiredAbandonedAsync(DateTime.UtcNow.Subtract(AbandonedRetention))
                .ConfigureAwait(false);

            if (deleted > 0)
            {
                _logger.Info($"[Retry-MES] 已清理 {deleted} 条过期弃置 retry 记录。");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Retry-MES] 清理过期弃置记录失败：{ex.Message}");
        }
    }

    public async Task ApplyIdleOrBackoffStateAsync()
    {
        var pendingCount = await _retryStore.GetCountAsync().ConfigureAwait(false);
        _diagnosticsStore.SetRuntimeState(
            pendingCount > 0
                ? MesRetryRuntimeState.Backoff
                : MesRetryRuntimeState.Idle);
    }
}
