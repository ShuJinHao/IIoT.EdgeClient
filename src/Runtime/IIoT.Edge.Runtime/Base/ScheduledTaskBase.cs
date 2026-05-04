using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Runtime.Context;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Runtime.Base;

public abstract class ScheduledTaskBase : IBackgroundTask
{
    protected readonly ProductionContext? Context;
    protected readonly ILogService Logger;
    protected CancellationToken CurrentCancellationToken { get; private set; }

    public abstract string TaskName { get; }
    protected abstract int ExecuteInterval { get; }
    protected virtual int ErrorRetryInterval => 1000;
    protected virtual Task WaitForNextIterationAsync(CancellationToken ct)
        => Task.Delay(ExecuteInterval, ct);

    protected ScheduledTaskBase(ProductionContext context, ILogService logger)
    {
        Context = context;
        Logger = logger;
    }

    protected ScheduledTaskBase(ILogService logger)
    {
        Context = null;
        Logger = logger;
    }

    protected abstract Task ExecuteAsync();

    public async Task StartAsync(CancellationToken ct)
    {
        await Task.Factory.StartNew(
            () => TaskCoreAsync(ct),
            ct,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    private async Task TaskCoreAsync(CancellationToken ct)
    {
        CurrentCancellationToken = ct;
        var deviceInfo = Context is not null ? $"[{Context.DeviceName}] " : string.Empty;
        Logger.Info($"{deviceInfo}{TaskName} 已启动，执行间隔：{ExecuteInterval}ms");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ExecuteAsync();
                await WaitForNextIterationAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error($"{deviceInfo}{TaskName} 执行失败：{ex.Message}");
                try
                {
                    await Task.Delay(ErrorRetryInterval, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        Logger.Info($"{deviceInfo}{TaskName} 已停止。");
    }
}
