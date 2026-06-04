using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

internal abstract class RetryHousekeepingServiceBase<TRuntimeState>
    where TRuntimeState : struct, Enum
{
    private static readonly TimeSpan AbandonedRetention = TimeSpan.FromDays(30);

    private readonly ILogService _logger;
    private readonly IRetryRecordStore _retryStore;
    private readonly IRetryDiagnosticsStore<TRuntimeState> _diagnosticsStore;
    private readonly string _logPrefix;
    private readonly string _recoverMessage;
    private readonly TRuntimeState _idleState;
    private readonly TRuntimeState _backoffState;
    private DateOnly? _lastAbandonedCleanupDateUtc;

    protected RetryHousekeepingServiceBase(
        ILogService logger,
        IRetryRecordStore retryStore,
        IRetryDiagnosticsStore<TRuntimeState> diagnosticsStore,
        string logPrefix,
        string recoverMessage,
        TRuntimeState idleState,
        TRuntimeState backoffState)
    {
        _logger = logger;
        _retryStore = retryStore;
        _diagnosticsStore = diagnosticsStore;
        _logPrefix = logPrefix;
        _recoverMessage = recoverMessage;
        _idleState = idleState;
        _backoffState = backoffState;
    }

    public async Task RecoverAbandonedRecordsAsync()
    {
        try
        {
            await _retryStore.ResetAllAbandonedAsync().ConfigureAwait(false);
            _logger.Info($"[{_logPrefix}] {_recoverMessage}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"[{_logPrefix}] 重置弃置记录失败：{ex.Message}");
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
                _logger.Info($"[{_logPrefix}] 已清理 {deleted} 条过期弃置 retry 记录。");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"[{_logPrefix}] 清理过期弃置记录失败：{ex.Message}");
        }
    }

    public async Task ApplyIdleOrBackoffStateAsync()
    {
        var pendingCount = await _retryStore.GetCountAsync().ConfigureAwait(false);
        _diagnosticsStore.SetRuntimeState(pendingCount > 0 ? _backoffState : _idleState);
    }
}
