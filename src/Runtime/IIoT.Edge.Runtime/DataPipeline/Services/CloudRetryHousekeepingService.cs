using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

public interface ICloudRetryHousekeepingService
{
    Task RecoverAbandonedRecordsAsync();

    Task CleanupExpiredAbandonedRecordsAsync();

    Task ApplyIdleOrBackoffStateAsync();

    bool DidPauseForRecovery(CloudUploadDiagnosticsSnapshot previousSnapshot, string processType);
}

internal sealed class CloudRetryHousekeepingService : ICloudRetryHousekeepingService
{
    private readonly ILogService _logger;
    private readonly ICloudRetryRecordStore _retryStore;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private DateOnly? _lastAbandonedCleanupDateUtc;

    private static readonly TimeSpan AbandonedRetention = TimeSpan.FromDays(30);

    public CloudRetryHousekeepingService(
        ILogService logger,
        ICloudRetryRecordStore retryStore,
        ICloudUploadDiagnosticsStore diagnosticsStore)
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
            _logger.Info("[Retry-Cloud] 云端上传门控已恢复，弃置记录已重置为可补传。");
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Retry-Cloud] 重置弃置记录失败：{ex.Message}");
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
                _logger.Info($"[Retry-Cloud] 已清理 {deleted} 条过期弃置 retry 记录。");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Retry-Cloud] 清理过期弃置记录失败：{ex.Message}");
        }
    }

    public async Task ApplyIdleOrBackoffStateAsync()
    {
        var pendingCount = await _retryStore.GetCountAsync().ConfigureAwait(false);
        _diagnosticsStore.SetRuntimeState(
            pendingCount > 0
                ? CloudRetryRuntimeState.Backoff
                : CloudRetryRuntimeState.Idle);
    }

    public bool DidPauseForRecovery(CloudUploadDiagnosticsSnapshot previousSnapshot, string processType)
    {
        var currentSnapshot = _diagnosticsStore.Snapshot;
        if (!string.Equals(currentSnapshot.LastProcessType, processType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (currentSnapshot.LastAttemptAt == previousSnapshot.LastAttemptAt)
        {
            return false;
        }

        return ShouldPauseForRecovery(currentSnapshot);
    }

    private static bool ShouldPauseForRecovery(CloudUploadDiagnosticsSnapshot snapshot)
        => snapshot.LastOutcome is CloudCallOutcome.SkippedUploadNotReady or CloudCallOutcome.UnauthorizedAfterRetry;
}
