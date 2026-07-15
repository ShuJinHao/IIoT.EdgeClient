using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Abstractions.Logging;

namespace IIoT.Edge.Application.Common.Tasks;

public sealed class LongRunningBackgroundTaskGroupService : IManagedBackgroundService
{
    private readonly IReadOnlyList<LongRunningBackgroundTaskService> _services;

    public LongRunningBackgroundTaskGroupService(
        string serviceName,
        IEnumerable<IBackgroundTask> tasks,
        ILogService? logger = null)
    {
        ServiceName = serviceName;
        _services = tasks
            .Select(task => new LongRunningBackgroundTaskService(task, logger))
            .ToList()
            .AsReadOnly();
    }

    public string ServiceName { get; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var service in _services)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await service.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await StopAllBestEffortAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        List<Exception>? failures = null;
        for (var index = _services.Count - 1; index >= 0; index--)
        {
            try
            {
                await _services[index].StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is { Count: 1 })
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(failures);
        }
    }

    private async Task StopAllBestEffortAsync(CancellationToken cancellationToken)
    {
        for (var index = _services.Count - 1; index >= 0; index--)
        {
            try
            {
                await _services[index].StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 启动失败/取消的原始异常必须保持，清理异常不得覆盖它。
            }
        }
    }
}
