using System.Reflection;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Runtime;

namespace IIoT.Edge.Application.Features.Updates;

public sealed class EdgeRuntimeHeartbeatService(
    IEdgeUpdateConfigurationProvider configurationProvider,
    IEdgeUpdateDeviceSessionClient deviceSessionClient,
    IEdgeRuntimeHeartbeatReporter heartbeatReporter,
    ILocalSystemRuntimeConfigService runtimeConfig,
    ILogService logger) : IEdgeRuntimeHeartbeatService, IDisposable
{
    private readonly object _lifecycleLock = new();
    private readonly string _runtimeInstanceId = Guid.NewGuid().ToString("N");
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private EdgeUpdateTarget? _target;
    private bool _running;

    public Task StartAsync(
        EdgeUpdateTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_lifecycleLock)
        {
            if (_running)
            {
                return Task.CompletedTask;
            }

            _running = true;
            _target = target;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopTask = Task.Run(() => RunLoopAsync(target, _cts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? loopTask;
        EdgeUpdateTarget? target;

        lock (_lifecycleLock)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            cts = _cts;
            loopTask = _loopTask;
            target = _target;
            _cts = null;
            _loopTask = null;
            _target = null;
        }

        if (target is not null)
        {
            await ReportBestEffortAsync(target, EdgeRuntimeHeartbeatStatus.Stopping, cancellationToken)
                .ConfigureAwait(false);
        }

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            if (loopTask is not null)
            {
                try
                {
                    await loopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            cts.Dispose();
        }

        if (target is not null)
        {
            await ReportBestEffortAsync(target, EdgeRuntimeHeartbeatStatus.Stopped, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private async Task RunLoopAsync(EdgeUpdateTarget target, CancellationToken cancellationToken)
    {
        logger.Info("[运行心跳] 循环已启动。");
        await ReportBestEffortAsync(target, EdgeRuntimeHeartbeatStatus.Starting, cancellationToken)
            .ConfigureAwait(false);
        await ReportBestEffortAsync(target, EdgeRuntimeHeartbeatStatus.Running, cancellationToken)
            .ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ResolveInterval(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await ReportBestEffortAsync(target, EdgeRuntimeHeartbeatStatus.Running, cancellationToken)
                .ConfigureAwait(false);
        }

        logger.Info("[运行心跳] 循环已停止。");
    }

    private async Task ReportBestEffortAsync(
        EdgeUpdateTarget target,
        EdgeRuntimeHeartbeatStatus status,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!runtimeConfig.Current.SystemCloudEnabled)
            {
                logger.Debug("[运行心跳] Cloud 已关闭，跳过本次上报。");
                return;
            }

            var configuration = configurationProvider.Resolve(target);
            if (!configuration.Success || configuration.Options is null)
            {
                logger.Debug($"[运行心跳] CloudApi 配置不可用：{configuration.ErrorMessage}");
                return;
            }

            var session = await deviceSessionClient
                .BootstrapAsync(configuration.Options, cancellationToken)
                .ConfigureAwait(false);
            if (!session.Success || session.Value is null)
            {
                logger.Debug($"[运行心跳] Bootstrap 失败：{session.ErrorMessage}");
                return;
            }

            var result = await heartbeatReporter
                .ReportAsync(
                    configuration.Options,
                    session.Value,
                    new EdgeRuntimeHeartbeatReport(
                        _runtimeInstanceId,
                        target.MachineProfile,
                        ResolveHostVersion(target),
                        EdgeClientHostRuntime.HostApiVersion,
                        status,
                        _startedAtUtc,
                        DateTime.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                logger.Debug($"[运行心跳] 上报失败：{result.ErrorMessage}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.Debug($"[运行心跳] 上报异常：{ex.Message}");
        }
    }

    private TimeSpan ResolveInterval()
    {
        var interval = runtimeConfig.Current.RuntimeHeartbeatInterval;
        if (interval < TimeSpan.FromSeconds(10) || interval > TimeSpan.FromHours(1))
        {
            return TimeSpan.FromSeconds(60);
        }

        return interval;
    }

    private static string ResolveHostVersion(EdgeUpdateTarget target)
    {
        var candidates = new[]
        {
            Path.Combine(target.HostDirectory, "IIoT.Edge.Host.Bootstrap.dll"),
            Path.Combine(target.HostDirectory, "IIoT.Edge.Shell.dll"),
            target.HostExecutablePath
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                {
                    continue;
                }

                var assemblyName = AssemblyName.GetAssemblyName(candidate);
                return EdgeClientHostRuntime.FormatHostVersion(assemblyName.Version);
            }
            catch (BadImageFormatException)
            {
            }
            catch (FileLoadException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return EdgeClientHostRuntime.FormatHostVersion(Assembly.GetEntryAssembly()?.GetName().Version);
    }
}
