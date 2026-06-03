using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Logging;

namespace IIoT.Edge.Infrastructure.Integration.Mes;

public sealed class MesHeartbeatTask
{
    private static readonly TimeSpan DisabledInterval = TimeSpan.FromSeconds(10);

    private readonly IMesHeartbeatProbe _probe;
    private readonly IExternalHeartbeatStateStore _stateStore;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly ILogService _logger;
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private bool _isRunning;

    public MesHeartbeatTask(
        IMesHeartbeatProbe probe,
        IExternalHeartbeatStateStore stateStore,
        ILocalSystemRuntimeConfigService runtimeConfig,
        ILogService logger)
    {
        _probe = probe;
        _stateStore = stateStore;
        _runtimeConfig = runtimeConfig;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            if (_isRunning)
            {
                return Task.CompletedTask;
            }

            _isRunning = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? localCts;
        Task? localWorker;

        lock (_lifecycleLock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            localCts = _cts;
            localWorker = _worker;
            _cts = null;
            _worker = null;
        }

        if (localCts is null)
        {
            return;
        }

        await localCts.CancelAsync().ConfigureAwait(false);
        if (localWorker is not null)
        {
            try
            {
                await localWorker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        localCts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.Info("[MES] 心跳循环已启动。");
        while (!cancellationToken.IsCancellationRequested)
        {
            await ProbeOnceAsync(cancellationToken).ConfigureAwait(false);

            var interval = _runtimeConfig.Current.MesUploadEnabled
                ? _runtimeConfig.Current.OnlineHeartbeatInterval
                : DisabledInterval;

            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.Info("[MES] 心跳循环已停止。");
    }

    private async Task ProbeOnceAsync(CancellationToken cancellationToken)
    {
        if (!_runtimeConfig.Current.MesUploadEnabled)
        {
            _stateStore.MarkNotReady(ExternalSystemKind.Mes, "mes_upload_disabled", "MES 上传已关闭。");
            return;
        }

        try
        {
            var snapshot = await _probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.IsReady)
            {
                _stateStore.MarkReady(
                    ExternalSystemKind.Mes,
                    snapshot.LastSuccessAtUtc,
                    snapshot.Message,
                    snapshot.LatencyMs);
                return;
            }

            _stateStore.MarkNotReady(
                ExternalSystemKind.Mes,
                snapshot.ReasonCode,
                snapshot.Message,
                snapshot.LastFailureAtUtc ?? snapshot.LastAttemptAtUtc,
                snapshot.LatencyMs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _stateStore.MarkNotReady(ExternalSystemKind.Mes, "mes_heartbeat_exception", ex.Message);
            _logger.Warn($"[MES] 心跳探测失败：{ex.Message}");
        }
    }
}
