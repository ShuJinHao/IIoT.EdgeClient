using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Module.Sdk.Base;

public abstract class ScheduledTaskBase : IStartupAwareBackgroundTask
{
    protected readonly ProductionContext? Context;
    protected readonly ILogService Logger;
    protected CancellationToken CurrentCancellationToken { get; private set; }

    public abstract string TaskName { get; }
    protected abstract int ExecuteInterval { get; }
    protected virtual int ErrorRetryInterval => 1000;
    protected virtual Task WaitForNextIterationAsync(CancellationToken ct)
        => Task.Delay(ExecuteInterval, ct);
    protected virtual Task WaitForErrorRetryAsync(CancellationToken ct)
        => Task.Delay(ErrorRetryInterval, ct);
    protected virtual bool ShouldPropagateExecutionFailure(
        Exception exception,
        CancellationToken cancellationToken)
        => false;

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

    public Task StartAsync(CancellationToken ct)
        => StartWithStartup(ct).Execution;

    public BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken)
    {
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = TaskCoreAsync(cancellationToken, startup);
        return new BackgroundTaskRun(startup.Task, execution);
    }

    private async Task TaskCoreAsync(CancellationToken ct, TaskCompletionSource startup)
    {
        CurrentCancellationToken = ct;
        var deviceInfo = Context is not null ? $"[{Context.DeviceName}] " : string.Empty;
        Logger.Info($"{deviceInfo}{TaskName} 已启动，执行间隔：{ExecuteInterval}ms");
        startup.TrySetResult();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ExecuteAsync();
                await WaitForNextIterationAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error($"{deviceInfo}{TaskName} 执行失败：{ex.Message}");
                if (ShouldPropagateExecutionFailure(ex, ct))
                {
                    throw;
                }

                try
                {
                    await WaitForErrorRetryAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception retryException)
                {
                    Logger.Error($"{deviceInfo}{TaskName} 重试等待失败：{retryException.Message}");
                }
            }
        }

        Logger.Info($"{deviceInfo}{TaskName} 已停止。");
    }
}
