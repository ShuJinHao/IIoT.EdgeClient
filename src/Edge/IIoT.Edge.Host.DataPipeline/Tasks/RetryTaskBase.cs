using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Sdk.Base;
using IIoT.Edge.Host.DataPipeline.Services;

using IIoT.Edge.Module.Contracts.Shared;
namespace IIoT.Edge.Host.DataPipeline.Tasks;

public abstract class RetryTaskBase<TRuntimeState, TProcessResult> : ScheduledTaskBase
    where TRuntimeState : struct, Enum
{
    private readonly IRetryDiagnosticsStore<TRuntimeState> _diagnosticsStore;
    private readonly IRetryTaskFallbackRecoveryService _fallbackRecoveryService;
    private readonly IRetryTaskRecordProcessor<TProcessResult> _retryRecordProcessor;
    private readonly IRetryTaskHousekeepingService _housekeepingService;
    private readonly TRuntimeState _retryingState;
    private readonly TimeSpan _retryInterval;
    private bool _wasUnavailable = true;
    private DateTimeOffset _nextScheduledRunUtc = DateTimeOffset.MinValue;
    private int _forceIteration;

    protected RetryTaskBase(
        ILogService logger,
        IRetryDiagnosticsStore<TRuntimeState> diagnosticsStore,
        IRetryTaskFallbackRecoveryService fallbackRecoveryService,
        IRetryTaskRecordProcessor<TProcessResult> retryRecordProcessor,
        IRetryTaskHousekeepingService housekeepingService,
        TRuntimeState retryingState,
        DataPipelineRetryScheduleOptions? scheduleOptions = null)
        : base(logger)
    {
        _diagnosticsStore = diagnosticsStore;
        _fallbackRecoveryService = fallbackRecoveryService;
        _retryRecordProcessor = retryRecordProcessor;
        _housekeepingService = housekeepingService;
        _retryingState = retryingState;
        _retryInterval = (scheduleOptions ?? new DataPipelineRetryScheduleOptions()).GetInterval();
    }

    protected virtual bool SetRetryingBeforeRecovery => false;

    internal Task ExecuteOneIterationAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _forceIteration, 1);
        return ExecuteAsync().WaitAsync(ct);
    }

    protected override async Task ExecuteAsync()
    {
        var availability = await CheckAvailabilityAsync(CurrentCancellationToken).ConfigureAwait(false);
        if (!availability.CanRun)
        {
            if (availability.RefreshCapacityBeforeState)
            {
                await RefreshCapacityStatusAsync().ConfigureAwait(false);
            }

            SetRuntimeState(availability.UnavailableState);
            _wasUnavailable = true;
            return;
        }

        var forceIteration = Interlocked.Exchange(ref _forceIteration, 0) != 0;
        var now = DateTimeOffset.UtcNow;
        if (!forceIteration && !_wasUnavailable && now < _nextScheduledRunUtc)
        {
            return;
        }

        _nextScheduledRunUtc = now.Add(_retryInterval);

        if (SetRetryingBeforeRecovery)
        {
            SetRuntimeState(_retryingState);
        }

        if (_wasUnavailable)
        {
            _wasUnavailable = false;
            await _housekeepingService.RecoverAbandonedRecordsAsync().ConfigureAwait(false);
        }

        await _housekeepingService.CleanupExpiredAbandonedRecordsAsync().ConfigureAwait(false);
        await _fallbackRecoveryService.RecoverAsync().ConfigureAwait(false);

        if (!SetRetryingBeforeRecovery)
        {
            SetRuntimeState(_retryingState);
        }

        var retryResult = await _retryRecordProcessor
            .ProcessAsync(CurrentCancellationToken)
            .ConfigureAwait(false);
        if (!await HandleRetryResultAsync(retryResult).ConfigureAwait(false))
        {
            return;
        }

        if (!await AfterRetryProcessingAsync().ConfigureAwait(false))
        {
            return;
        }

        await RefreshCapacityStatusAsync().ConfigureAwait(false);
        await _housekeepingService.ApplyIdleOrBackoffStateAsync().ConfigureAwait(false);
    }

    protected void SetRuntimeState(TRuntimeState state)
        => _diagnosticsStore.SetRuntimeState(state);

    protected abstract ValueTask<RetryTaskAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);

    protected virtual Task<bool> HandleRetryResultAsync(TProcessResult result)
        => Task.FromResult(true);

    protected virtual Task<bool> AfterRetryProcessingAsync()
        => Task.FromResult(true);

    protected abstract Task RefreshCapacityStatusAsync();

    protected readonly record struct RetryTaskAvailability(
        bool CanRun,
        TRuntimeState UnavailableState,
        bool RefreshCapacityBeforeState = false)
    {
        public static RetryTaskAvailability Available()
            => new(true, default);

        public static RetryTaskAvailability Unavailable(
            TRuntimeState unavailableState,
            bool refreshCapacityBeforeState = false)
            => new(false, unavailableState, refreshCapacityBeforeState);
    }
}
