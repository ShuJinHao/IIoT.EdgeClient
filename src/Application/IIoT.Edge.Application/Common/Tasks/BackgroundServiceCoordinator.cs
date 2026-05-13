using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Tasks;

namespace IIoT.Edge.Application.Common.Tasks;

public sealed class BackgroundServiceCoordinator : IBackgroundServiceCoordinator
{
    private readonly IReadOnlyList<IManagedBackgroundService> _services;
    private readonly ILogService _logger;
    private readonly List<IManagedBackgroundService> _startedServices = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;

    public BackgroundServiceCoordinator(
        IEnumerable<IManagedBackgroundService> services,
        ILogService logger)
    {
        _services = services.ToList().AsReadOnly();
        _logger = logger;
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

            foreach (var service in _services)
            {
                _logger.Info($"[Background] Starting {service.ServiceName}...");
                try
                {
                    await service.StartAsync(cancellationToken);
                    _startedServices.Add(service);
                    _logger.Info($"[Background] Started {service.ServiceName}.");
                }
                catch (Exception ex)
                {
                    _logger.Error($"[Background] Failed to start {service.ServiceName}: {ex.Message}");
                    await StopStartedServicesCoreAsync(cancellationToken);
                    throw;
                }
            }

            _started = true;
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
            await StopStartedServicesCoreAsync(cancellationToken);
            _started = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopStartedServicesCoreAsync(CancellationToken cancellationToken)
    {
        for (var index = _startedServices.Count - 1; index >= 0; index--)
        {
            var service = _startedServices[index];
            try
            {
                _logger.Info($"[Background] Stopping {service.ServiceName}...");
                await service.StopAsync(cancellationToken);
                _logger.Info($"[Background] Stopped {service.ServiceName}.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[Background] Failed to stop {service.ServiceName}: {ex.Message}");
            }
        }

        _startedServices.Clear();
    }
}
