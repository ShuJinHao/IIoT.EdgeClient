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
            : $"后台服务启动失败：{string.Join("；", failures.Select(static failure => $"{failure.ServiceName}（{failure.Exception.Message}）"))}";
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
            : $"后台服务停止失败：{string.Join("；", failures.Select(static failure => $"{failure.ServiceName}（{failure.Exception.Message}）"))}";
}

public sealed class BackgroundServiceCoordinatorOptions
{
    public static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(10);

    public TimeSpan StartupTimeout { get; init; } = DefaultStartupTimeout;

    public TimeSpan StopTimeout { get; init; } = DefaultStopTimeout;
}

public sealed class BackgroundServiceCoordinator : IBackgroundServiceCoordinator
{
    private readonly IReadOnlyList<IManagedBackgroundService> _services;
    private readonly ILogService _logger;
    private readonly BackgroundServiceCoordinatorOptions _options;
    private readonly List<IManagedBackgroundService> _startedServices = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;

    public BackgroundServiceCoordinator(
        IEnumerable<IManagedBackgroundService> services,
        ILogService logger)
        : this(services, logger, new BackgroundServiceCoordinatorOptions())
    {
    }

    public BackgroundServiceCoordinator(
        IEnumerable<IManagedBackgroundService> services,
        ILogService logger,
        BackgroundServiceCoordinatorOptions options)
    {
        _services = services.ToList().AsReadOnly();
        _logger = logger;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ValidateTimeout(options.StartupTimeout, nameof(options.StartupTimeout));
        ValidateTimeout(options.StopTimeout, nameof(options.StopTimeout));
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
                    _logger.Error($"[后台服务] 启动 {service.ServiceName} 失败：{ex.Message}");
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
            ObserveLateStop(service, stopTask);
            var exception = new TimeoutException(
                $"后台服务 {service.ServiceName} 未在 {_options.StopTimeout} 内停止。");
            _logger.Error($"[后台服务] 停止 {service.ServiceName} 失败：{exception.Message}");
            return exception;
        }
        catch (Exception ex)
        {
            _logger.Error($"[后台服务] 停止 {service.ServiceName} 失败：{ex.Message}");
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
            _logger.Error($"[后台服务] 启动取消后清理失败：{ex.Message}");
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
            _logger.Error($"[后台服务] {service.ServiceName} 超时后的停止任务最终失败：{ex.Message}");
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
            _logger.Error($"[后台服务] 超时后迟到启动的 {service.ServiceName} 未能停止：{failure.Message}");
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
}
