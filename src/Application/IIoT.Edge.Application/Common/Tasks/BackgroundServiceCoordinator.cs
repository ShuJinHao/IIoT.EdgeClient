using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Tasks;

namespace IIoT.Edge.Application.Common.Tasks;

public sealed record BackgroundServiceStartFailure(
    string ServiceName,
    Exception Exception);

public sealed class BackgroundServiceStartException(
    IReadOnlyList<BackgroundServiceStartFailure> failures)
    : Exception(BuildMessage(failures), failures.FirstOrDefault()?.Exception)
{
    public IReadOnlyList<BackgroundServiceStartFailure> Failures { get; } =
        failures.Count == 0
            ? throw new ArgumentException("至少需要一条后台服务启动失败记录。", nameof(failures))
            : failures;

    public string ServiceName => Failures[0].ServiceName;

    private static string BuildMessage(IReadOnlyList<BackgroundServiceStartFailure> failures)
        => failures.Count == 0
            ? "后台服务启动失败。"
            : $"后台服务启动失败：{string.Join("；", failures.Select(static failure => $"{failure.ServiceName}（{failure.Exception.GetType().Name}）"))}";
}

public sealed record BackgroundServiceStopFailure(
    string ServiceName,
    Exception Exception);

public sealed class BackgroundServiceStopException(
    IReadOnlyList<BackgroundServiceStopFailure> failures)
    : Exception(BuildMessage(failures), failures.FirstOrDefault()?.Exception)
{
    public IReadOnlyList<BackgroundServiceStopFailure> Failures { get; } =
        failures.Count == 0
            ? throw new ArgumentException("至少需要一条后台服务停止失败记录。", nameof(failures))
            : failures;

    private static string BuildMessage(IReadOnlyList<BackgroundServiceStopFailure> failures)
        => failures.Count == 0
            ? "后台服务停止失败。"
            : $"后台服务停止失败：{string.Join("；", failures.Select(static failure => $"{failure.ServiceName}（{failure.Exception.GetType().Name}）"))}";
}

public sealed class BackgroundServiceCoordinatorOptions
{
    public static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultRecoveryInterval = TimeSpan.FromSeconds(5);

    public TimeSpan StartupTimeout { get; init; } = DefaultStartupTimeout;

    public TimeSpan StopTimeout { get; init; } = DefaultStopTimeout;

    public TimeSpan RecoveryInterval { get; init; } = DefaultRecoveryInterval;
}

public sealed class BackgroundServiceCoordinator : IBackgroundServiceCoordinator
{
    private static readonly string[] RecoverableServiceNames =
    [
        "ProcessQueueTask",
        "CloudRetryTask",
        "MesRetryTask"
    ];

    private readonly IReadOnlyList<IManagedBackgroundService> _services;
    private readonly ILogService _logger;
    private readonly BackgroundServiceCoordinatorOptions _options;
    private readonly IBackgroundServiceRuntimeStatusReader? _runtimeStatus;
    private readonly IBackgroundServiceRuntimeStatusWriter? _runtimeStatusWriter;
    private readonly List<IManagedBackgroundService> _startedServices = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        IManagedBackgroundService,
        Task> _pendingStopTasks = new(ReferenceEqualityComparer.Instance);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _recoveryCancellation;
    private Task? _recoveryTask;
    private bool _started;

    public BackgroundServiceCoordinator(
        IEnumerable<IManagedBackgroundService> services,
        ILogService logger)
        : this(
            services,
            logger,
            new BackgroundServiceCoordinatorOptions(),
            runtimeStatus: null)
    {
    }

    public BackgroundServiceCoordinator(
        IEnumerable<IManagedBackgroundService> services,
        ILogService logger,
        BackgroundServiceCoordinatorOptions options)
        : this(services, logger, options, runtimeStatus: null)
    {
    }

    public BackgroundServiceCoordinator(
        IEnumerable<IManagedBackgroundService> services,
        ILogService logger,
        BackgroundServiceCoordinatorOptions options,
        IBackgroundServiceRuntimeStatusReader? runtimeStatus)
    {
        _services = services.ToList().AsReadOnly();
        _logger = logger;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runtimeStatus = runtimeStatus;
        _runtimeStatusWriter = runtimeStatus as IBackgroundServiceRuntimeStatusWriter;
        ValidateTimeout(options.StartupTimeout, nameof(options.StartupTimeout));
        ValidateTimeout(options.StopTimeout, nameof(options.StopTimeout));
        ValidateRecoveryInterval(options.RecoveryInterval);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_started)
            {
                return;
            }

            var failures = new List<BackgroundServiceStartFailure>();
            foreach (var service in _services)
            {
                _logger.Info($"[后台服务] 正在启动 {service.ServiceName}。");
                try
                {
                    await StartServiceWithDeadlineAsync(service, cancellationToken).ConfigureAwait(false);
                    _startedServices.Add(service);
                    _logger.Info($"[后台服务] 已启动 {service.ServiceName}。");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await StopStartedServicesBestEffortAsync().ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error($"[后台服务] 启动 {service.ServiceName} 失败（{ex.GetType().Name}）。");
                    var cleanupFailure = await TryStopServiceAsync(service, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (cleanupFailure is not null)
                    {
                        _startedServices.Add(service);
                        ex = new AggregateException(ex, cleanupFailure);
                    }

                    failures.Add(new BackgroundServiceStartFailure(service.ServiceName, ex));
                }
            }

            _started = true;
            StartRecoverySupervisor();
            if (failures.Count > 0)
            {
                throw new BackgroundServiceStartException(failures);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopRecoverySupervisorAsync().ConfigureAwait(false);
            var failures = await StopStartedServicesCoreAsync(cancellationToken).ConfigureAwait(false);
            if (failures.Count > 0)
                throw new BackgroundServiceStopException(failures);

            _started = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StartRecoverySupervisor()
    {
        if (_runtimeStatus is null
            || !_services.Any(service => IsRecoverableServiceName(service.ServiceName))
            || _recoveryTask is { IsCompleted: false })
        {
            return;
        }

        _recoveryCancellation?.Dispose();
        _recoveryCancellation = new CancellationTokenSource();
        _recoveryTask = RunRecoverySupervisorAsync(_recoveryCancellation.Token);
    }

    private async Task StopRecoverySupervisorAsync()
    {
        var cancellation = _recoveryCancellation;
        var recoveryTask = _recoveryTask;
        _recoveryCancellation = null;
        _recoveryTask = null;
        if (cancellation is null || recoveryTask is null)
        {
            cancellation?.Dispose();
            return;
        }

        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[后台服务] 取消故障恢复监督失败（{ex.GetType().Name}）。");
        }

        try
        {
            await recoveryTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error($"[后台服务] 故障恢复监督退出失败（{ex.GetType().Name}）。");
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task RunRecoverySupervisorAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(_options.RecoveryInterval, cancellationToken).ConfigureAwait(false);
            try
            {
                await RecoverFaultedServicesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"[后台服务] 故障恢复轮询失败（{ex.GetType().Name}）。");
            }
        }
    }

    private async Task RecoverFaultedServicesAsync(CancellationToken cancellationToken)
    {
        if (_runtimeStatus is null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started)
            {
                return;
            }

            foreach (var pendingService in _pendingStopTasks.Keys)
                PublishRecoverableStopTimeout(pendingService);

            var faultedServiceNames = _runtimeStatus.GetAll()
                .Where(static snapshot => snapshot.State == BackgroundServiceRuntimeState.Faulted)
                .Select(static snapshot => snapshot.ServiceName)
                .Where(IsRecoverableServiceName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var serviceName in faultedServiceNames)
            {
                var service = _services.FirstOrDefault(candidate => string.Equals(
                    candidate.ServiceName,
                    serviceName,
                    StringComparison.OrdinalIgnoreCase));
                if (service is null)
                {
                    continue;
                }

                if (_pendingStopTasks.ContainsKey(service))
                    continue;

                _logger.Info($"[后台服务] 正在恢复 {service.ServiceName}。");
                try
                {
                    await StartServiceWithDeadlineAsync(service, cancellationToken).ConfigureAwait(false);
                    if (!_startedServices.Contains(service))
                    {
                        _startedServices.Add(service);
                    }
                    _logger.Info($"[后台服务] 已恢复 {service.ServiceName}。");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error($"[后台服务] 恢复 {service.ServiceName} 失败（{ex.GetType().Name}）。");
                    var cleanupFailure = await TryStopServiceAsync(
                            service,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    if (cleanupFailure is not null && !_startedServices.Contains(service))
                    {
                        _startedServices.Add(service);
                    }
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<BackgroundServiceStopFailure>> StopStartedServicesCoreAsync(
        CancellationToken cancellationToken)
    {
        var failures = new List<BackgroundServiceStopFailure>();
        for (var index = _startedServices.Count - 1; index >= 0; index--)
        {
            var service = _startedServices[index];
            var failure = await TryStopServiceAsync(service, cancellationToken).ConfigureAwait(false);
            if (failure is null)
            {
                _startedServices.RemoveAt(index);
            }
            else
            {
                failures.Add(new BackgroundServiceStopFailure(service.ServiceName, failure));
            }
        }

        return failures;
    }

    private async Task StartServiceWithDeadlineAsync(
        IManagedBackgroundService service,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var startTask = service.StartAsync(deadline.Token);
        try
        {
            await startTask
                .WaitAsync(_options.StartupTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) when (!startTask.IsCompleted)
        {
            await CancelDeadlineBestEffortAsync(deadline).ConfigureAwait(false);
            ObserveLateStartAndStop(service, startTask);
            throw new TimeoutException(
                $"后台服务 {service.ServiceName} 未在 {_options.StartupTimeout} 内完成启动握手。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!startTask.IsCompleted)
                ObserveLateStartAndStop(service, startTask);
            throw;
        }
    }

    private async Task<Exception?> TryStopServiceAsync(
        IManagedBackgroundService service,
        CancellationToken cancellationToken)
    {
        if (_pendingStopTasks.ContainsKey(service))
        {
            PublishRecoverableStopTimeout(service);
            return new TimeoutException(
                $"后台服务 {service.ServiceName} 的上一次停止仍未完成。");
        }

        _logger.Info($"[后台服务] 正在停止 {service.ServiceName}。");
        Task? stopTask = null;
        try
        {
            stopTask = service.StopAsync(cancellationToken);
            await stopTask
                .WaitAsync(_options.StopTimeout, cancellationToken)
                .ConfigureAwait(false);
            _logger.Info($"[后台服务] 已停止 {service.ServiceName}。");
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException) when (stopTask is { IsCompleted: false })
        {
            _pendingStopTasks[service] = stopTask;
            PublishRecoverableStopTimeout(service);
            ObserveLateStop(service, stopTask);
            var exception = new TimeoutException(
                $"后台服务 {service.ServiceName} 未在 {_options.StopTimeout} 内停止。");
            _logger.Error($"[后台服务] 停止 {service.ServiceName} 失败（{exception.GetType().Name}）。");
            return exception;
        }
        catch (Exception ex)
        {
            _logger.Error($"[后台服务] 停止 {service.ServiceName} 失败（{ex.GetType().Name}）。");
            return ex;
        }
    }

    private async Task StopStartedServicesBestEffortAsync()
    {
        try
        {
            await StopStartedServicesCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[后台服务] 启动取消后清理失败（{ex.GetType().Name}）。");
        }
    }

    private void ObserveLateStartAndStop(IManagedBackgroundService service, Task startTask)
        => _ = ObserveLateStartAndStopAsync(service, startTask);

    private void ObserveLateStop(IManagedBackgroundService service, Task stopTask)
        => _ = ObserveLateStopAsync(service, stopTask);

    private async Task ObserveLateStopAsync(IManagedBackgroundService service, Task stopTask)
    {
        try
        {
            await stopTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[后台服务] {service.ServiceName} 超时后的停止任务最终失败（{ex.GetType().Name}）。");
        }
        finally
        {
            PublishRecoverableStopTimeout(service);
            _pendingStopTasks.TryRemove(service, out _);
        }
    }

    private void PublishRecoverableStopTimeout(IManagedBackgroundService service)
    {
        if (_runtimeStatusWriter is null
            || !IsRecoverableServiceName(service.ServiceName))
        {
            return;
        }

        if (_runtimeStatus?.TryGet(service.ServiceName, out var current) == true
            && current.State == BackgroundServiceRuntimeState.Faulted)
        {
            return;
        }

        try
        {
            _runtimeStatusWriter.Set(
                service.ServiceName,
                BackgroundServiceRuntimeState.Faulted,
                "BACKGROUND_TASK_STOP_TIMEOUT");
        }
        catch (Exception ex)
        {
            _logger.Error($"[后台服务] 更新 {service.ServiceName} 停止超时状态失败（{ex.GetType().Name}）。");
        }
    }

    private async Task ObserveLateStartAndStopAsync(
        IManagedBackgroundService service,
        Task startTask)
    {
        try
        {
            await startTask.ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        var failure = await TryStopServiceAsync(service, CancellationToken.None).ConfigureAwait(false);
        if (failure is not null)
        {
            _logger.Error($"[后台服务] 超时后迟到启动的 {service.ServiceName} 未能停止（{failure.GetType().Name}）。");
        }
    }

    private static async Task CancelDeadlineBestEffortAsync(CancellationTokenSource deadline)
    {
        try
        {
            await deadline.CancelAsync().ConfigureAwait(false);
        }
        catch
        {
            // 超时异常是主诊断，取消回调不得覆盖它。
        }
    }

    private static void ValidateTimeout(TimeSpan timeout, string parameterName)
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName, "后台服务超时不得为负数。");
    }

    private static void ValidateRecoveryInterval(TimeSpan recoveryInterval)
    {
        if (recoveryInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BackgroundServiceCoordinatorOptions.RecoveryInterval),
                "后台服务故障恢复间隔必须大于零。");
        }
    }

    private static bool IsRecoverableServiceName(string serviceName)
        => RecoverableServiceNames.Contains(serviceName, StringComparer.OrdinalIgnoreCase);
}
