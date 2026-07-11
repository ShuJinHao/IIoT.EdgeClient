using IIoT.Edge.Application.Abstractions.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

/// <summary>
/// 单个 PLC service 的异步操作门和终止状态机。
/// Closing 会先取消 lifetime token；只有 waiter 与 active lease 全部退出后才释放同步原语。
/// </summary>
internal sealed class PlcOperationGate
{
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _serviceName;
    private readonly TimeSpan _operationSettleTimeout;
    private readonly TimeSpan _disposeTimeout;

    private GateState _state = GateState.Open;
    private int _waiterCount;
    private int _activeLeaseCount;
    private string? _quarantineDetail;
    private Task? _lifetimeCancellationTask;
    private Task? _shutdownTask;

    public PlcOperationGate(
        string serviceName,
        TimeSpan operationSettleTimeout,
        TimeSpan disposeTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        if (operationSettleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(operationSettleTimeout));
        }

        if (disposeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(disposeTimeout));
        }

        _serviceName = serviceName;
        _operationSettleTimeout = operationSettleTimeout;
        _disposeTimeout = disposeTimeout;
    }

    public bool IsOpen
    {
        get
        {
            lock (_stateLock)
            {
                return _state == GateState.Open;
            }
        }
    }

    public void ThrowIfNotOpen(string operationName)
    {
        lock (_stateLock)
        {
            if (_state == GateState.Open)
            {
                return;
            }

            throw CreateClosedExceptionLocked(operationName);
        }
    }

    public static bool ShouldWrapOperationException(Exception exception)
        => exception is not TimeoutException
           and not OperationCanceledException
           and not ObjectDisposedException
           and not PlcServiceQuarantinedException;

    public async Task<TResult> ExecuteAsync<TResult>(
        string operationName,
        Func<CancellationToken, Task<TResult>> operationFactory,
        TimeSpan timeout,
        Func<Task> abortTransportAsync,
        Func<Task<TResult>, Task> releaseAfterAbortAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ArgumentNullException.ThrowIfNull(operationFactory);
        ArgumentNullException.ThrowIfNull(abortTransportAsync);
        ArgumentNullException.ThrowIfNull(releaseAfterAbortAsync);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var lease = await EnterAsync(operationName, cancellationToken).ConfigureAwait(false);
        var leaseTransferred = false;
        try
        {
            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
            operationCts.CancelAfter(timeout);
            var operationTask = operationFactory(operationCts.Token)
                ?? throw new InvalidOperationException($"{_serviceName} 的 {operationName} 未返回 Task。");

            try
            {
                return await operationTask
                    .WaitAsync(operationCts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception operationException) when (
                operationException is TimeoutException
                || operationException is OperationCanceledException && operationCts.IsCancellationRequested)
            {
                var timedOut = operationException is TimeoutException || !lease.Token.IsCancellationRequested;
                var quarantine = await AbortAndSettleAsync(
                        operationName,
                        operationTask,
                        abortTransportAsync,
                        releaseAfterAbortAsync,
                        lease)
                    .ConfigureAwait(false);
                leaseTransferred = quarantine.LeaseTransferred;
                if (quarantine.Exception is not null)
                {
                    throw quarantine.Exception;
                }

                if (timedOut)
                {
                    throw new TimeoutException(
                        $"{_serviceName} 的 {operationName} 超过 {timeout.TotalMilliseconds:0}ms。",
                        operationException);
                }

                cancellationToken.ThrowIfCancellationRequested();
                throw CreateClosedException(operationName);
            }
        }
        finally
        {
            if (!leaseTransferred)
            {
                lease.Dispose();
            }
        }
    }

    public Task ExecuteAsync(
        string operationName,
        Func<CancellationToken, Task> operationFactory,
        TimeSpan timeout,
        Func<Task> abortTransportAsync,
        Func<Task, Task> releaseAfterAbortAsync,
        CancellationToken cancellationToken = default)
        => ExecuteAsync<object?>(
            operationName,
            async token =>
            {
                await operationFactory(token).ConfigureAwait(false);
                return null;
            },
            timeout,
            abortTransportAsync,
            async task =>
            {
                await releaseAfterAbortAsync(task).ConfigureAwait(false);
            },
            cancellationToken);

    public async ValueTask DisposeAsync(Func<Task> releaseResourcesAsync)
    {
        ArgumentNullException.ThrowIfNull(releaseResourcesAsync);

        Task shutdownTask;
        TaskCompletionSource? shutdownStarter = null;
        lock (_stateLock)
        {
            if (_state == GateState.Disposed)
            {
                return;
            }

            if (_shutdownTask is null)
            {
                shutdownStarter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _shutdownTask = shutdownStarter.Task;
            }

            shutdownTask = _shutdownTask;
        }

        if (shutdownStarter is not null)
        {
            _ = CompleteShutdownAsync(shutdownStarter, releaseResourcesAsync);
        }

        try
        {
            await shutdownTask.WaitAsync(_disposeTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw MarkQuarantined(
                "DisposeAsync",
                $"释放超过硬上限 {_disposeTimeout.TotalMilliseconds:0}ms。",
                ex);
        }
        catch (PlcServiceQuarantinedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw MarkQuarantined("DisposeAsync", "资源释放失败。", ex);
        }
    }

    private async Task<Lease> EnterAsync(string operationName, CancellationToken cancellationToken)
    {
        CancellationTokenSource linkedCts;
        lock (_stateLock)
        {
            if (_state != GateState.Open)
            {
                throw CreateClosedExceptionLocked(operationName);
            }

            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token);
            _waiterCount++;
        }

        var waiterCounted = true;
        try
        {
            await _semaphore.WaitAsync(linkedCts.Token).ConfigureAwait(false);

            GateState stateAfterEnter;
            lock (_stateLock)
            {
                _waiterCount--;
                waiterCounted = false;
                _activeLeaseCount++;
                stateAfterEnter = _state;
            }

            var lease = new Lease(this, linkedCts);
            if (stateAfterEnter == GateState.Open)
            {
                return lease;
            }

            lease.Dispose();
            throw CreateClosedException(operationName);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (waiterCounted)
            {
                OnWaiterExited();
            }

            linkedCts.Dispose();
            throw CreateClosedException(operationName);
        }
        catch
        {
            if (waiterCounted)
            {
                OnWaiterExited();
            }

            linkedCts.Dispose();
            throw;
        }
    }

    private async Task<QuarantineResult> AbortAndSettleAsync<TResult>(
        string operationName,
        Task<TResult> operationTask,
        Func<Task> abortTransportAsync,
        Func<Task<TResult>, Task> releaseAfterAbortAsync,
        Lease lease)
    {
        var settlementTask = SettleAfterAbortAsync(
            operationTask,
            abortTransportAsync,
            releaseAfterAbortAsync);

        try
        {
            await settlementTask.WaitAsync(_operationSettleTimeout).ConfigureAwait(false);
            return QuarantineResult.None;
        }
        catch (TimeoutException ex)
        {
            var quarantineException = MarkQuarantined(
                operationName,
                $"transport 已中止，但第三方协议 Task 未在 {_operationSettleTimeout.TotalMilliseconds:0}ms 内退出。",
                ex);
            _ = ObserveQuarantinedSettlementAsync(settlementTask, lease);
            return new QuarantineResult(quarantineException, LeaseTransferred: true);
        }
        catch (Exception ex)
        {
            return new QuarantineResult(
                MarkQuarantined(operationName, "协议 Task 已退出，但隔离清理失败。", ex),
                LeaseTransferred: false);
        }
    }

    private static async Task SettleAfterAbortAsync<TResult>(
        Task<TResult> operationTask,
        Func<Task> abortTransportAsync,
        Func<Task<TResult>, Task> releaseAfterAbortAsync)
    {
        Task abortTask;
        try
        {
            abortTask = abortTransportAsync() ?? Task.CompletedTask;
        }
        catch (Exception ex)
        {
            abortTask = Task.FromException(ex);
        }

        var abortObservation = CaptureExceptionAsync(abortTask);
        var operationObservation = CaptureExceptionAsync(operationTask);
        var observed = await Task.WhenAll(abortObservation, operationObservation).ConfigureAwait(false);

        Exception? releaseException = null;
        try
        {
            await releaseAfterAbortAsync(operationTask).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            releaseException = ex;
        }

        if (observed[0] is not null || releaseException is not null)
        {
            throw new AggregateException(
                "PLC transport 中止或隔离清理失败。",
                new[] { observed[0], releaseException }.OfType<Exception>());
        }
    }

    private async Task CompleteShutdownAsync(
        TaskCompletionSource completion,
        Func<Task> releaseResourcesAsync)
    {
        try
        {
            var cancellationTask = BeginClosing();
            await ObserveAsync(cancellationTask).ConfigureAwait(false);
            await _drained.Task.ConfigureAwait(false);
            await releaseResourcesAsync().ConfigureAwait(false);
            CompleteDispose();
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private Task BeginClosing()
    {
        Task cancellationTask;
        lock (_stateLock)
        {
            if (_state == GateState.Open)
            {
                _state = GateState.Closing;
            }

            cancellationTask = EnsureLifetimeCancellationStartedLocked();
            SignalDrainedIfNeededLocked();
        }

        return cancellationTask;
    }

    private PlcServiceQuarantinedException MarkQuarantined(
        string operationName,
        string detail,
        Exception? innerException = null)
    {
        Task cancellationTask;
        lock (_stateLock)
        {
            if (_state != GateState.Disposed)
            {
                _state = GateState.Quarantined;
                _quarantineDetail ??= detail;
            }

            cancellationTask = EnsureLifetimeCancellationStartedLocked();
            SignalDrainedIfNeededLocked();
        }

        _ = ObserveAsync(cancellationTask);
        return new PlcServiceQuarantinedException(
            _serviceName,
            operationName,
            detail,
            innerException);
    }

    private Task EnsureLifetimeCancellationStartedLocked()
    {
        if (_lifetimeCancellationTask is not null)
        {
            return _lifetimeCancellationTask;
        }

        _lifetimeCancellationTask = _lifetimeCts.CancelAsync();
        return _lifetimeCancellationTask;
    }

    private async Task ObserveQuarantinedSettlementAsync(Task settlementTask, Lease lease)
    {
        try
        {
            await settlementTask.ConfigureAwait(false);
        }
        catch
        {
            // 隔离 continuation 只负责持续观察第三方 Task；诊断已由稳定隔离异常上报。
        }
        finally
        {
            lease.Dispose();
        }
    }

    private void CompleteDispose()
    {
        lock (_stateLock)
        {
            if (_waiterCount != 0 || _activeLeaseCount != 0)
            {
                throw new InvalidOperationException("PLC 操作门尚未排空，不能释放同步原语。");
            }

            _state = GateState.Disposed;
        }

        _semaphore.Dispose();
        _lifetimeCts.Dispose();
    }

    private Exception CreateClosedException(string operationName)
    {
        lock (_stateLock)
        {
            return CreateClosedExceptionLocked(operationName);
        }
    }

    private Exception CreateClosedExceptionLocked(string operationName)
        => _state == GateState.Quarantined
            ? new PlcServiceQuarantinedException(
                _serviceName,
                operationName,
                _quarantineDetail ?? "实例已隔离。")
            : new ObjectDisposedException(
                _serviceName,
                $"{_serviceName} 正在关闭或已释放，不能执行 {operationName}。");

    private void OnWaiterExited()
    {
        lock (_stateLock)
        {
            _waiterCount--;
            SignalDrainedIfNeededLocked();
        }
    }

    private void OnLeaseReleased()
    {
        lock (_stateLock)
        {
            _activeLeaseCount--;
            SignalDrainedIfNeededLocked();
        }
    }

    private void SignalDrainedIfNeededLocked()
    {
        if (_state != GateState.Open && _waiterCount == 0 && _activeLeaseCount == 0)
        {
            _drained.TrySetResult();
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async Task<Exception?> CaptureExceptionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class Lease : IDisposable
    {
        private PlcOperationGate? _owner;
        private CancellationTokenSource? _linkedCts;

        public Lease(PlcOperationGate owner, CancellationTokenSource linkedCts)
        {
            _owner = owner;
            _linkedCts = linkedCts;
        }

        public CancellationToken Token
            => _linkedCts?.Token
               ?? throw new ObjectDisposedException(nameof(Lease));

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
            {
                return;
            }

            Interlocked.Exchange(ref _linkedCts, null)?.Dispose();
            owner._semaphore.Release();
            owner.OnLeaseReleased();
        }
    }

    private readonly record struct QuarantineResult(
        PlcServiceQuarantinedException? Exception,
        bool LeaseTransferred)
    {
        public static QuarantineResult None { get; } = new(null, false);
    }

    private enum GateState
    {
        Open,
        Closing,
        Quarantined,
        Disposed
    }
}
