using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;

using IIoT.Edge.Module.Contracts.Shared;
namespace IIoT.Edge.Host.DataPipeline.Services;

internal abstract class RetryHousekeepingServiceBase<TRuntimeState>
    where TRuntimeState : struct, Enum
{
    private readonly ILogService _logger;
    private readonly IRetryRecordStore _retryStore;
    private readonly IRetryDiagnosticsStore<TRuntimeState> _diagnosticsStore;
    private readonly string _logPrefix;
    private readonly string _recoverMessage;
    private readonly TRuntimeState _idleState;
    private readonly TRuntimeState _backoffState;

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
            _logger.Warn(
                $"[{_logPrefix}] 结果=ClaimRecoveryFailed，" +
                $"异常类型={ex.GetType().Name}。");
        }
    }

    public Task CleanupExpiredAbandonedRecordsAsync()
        // 保留 Host API 2.0.x 调度点，但禁止对未成功 retry/fallback/deadletter 执行时间驱动硬删除。
        => Task.CompletedTask;

    public async Task ApplyIdleOrBackoffStateAsync()
    {
        var pendingCount = await _retryStore.GetCountAsync().ConfigureAwait(false);
        _diagnosticsStore.SetRuntimeState(pendingCount > 0 ? _backoffState : _idleState);
    }
}
