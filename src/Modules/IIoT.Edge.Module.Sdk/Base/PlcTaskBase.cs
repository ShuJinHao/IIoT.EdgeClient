using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Module.Sdk.Base;

public abstract class PlcTaskBase : IPlcTask, IStartupAwareBackgroundTask
{
    protected readonly IPlcBuffer Buffer;
    protected readonly ProductionContext Context;
    protected readonly ILogService Logger;
    protected CancellationToken TaskCancellationToken { get; private set; }

    public abstract string TaskName { get; }

    protected virtual int TaskLoopInterval => 10;
    protected virtual int ErrorRetryInterval => 1000;
    protected virtual Task WaitForNextIterationAsync(CancellationToken ct)
        => Task.Delay(TaskLoopInterval, ct);
    protected virtual Task WaitForErrorRetryAsync(CancellationToken ct)
        => Task.Delay(ErrorRetryInterval, ct);

    protected int Step
    {
        get => Context.GetStep(TaskName);
        set => Context.SetStep(TaskName, value);
    }

    protected PlcTaskBase(IPlcBuffer buffer, ProductionContext context, ILogService logger)
    {
        Buffer = buffer;
        Context = context;
        Logger = logger;
    }

    protected abstract Task DoCoreAsync();

    protected void SetTaskCancellationToken(CancellationToken cancellationToken)
    {
        TaskCancellationToken = cancellationToken;
    }

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
        SetTaskCancellationToken(ct);
        Logger.Info($"[{Context.DeviceName}] {TaskName} 已启动，当前步骤：{Step}");
        startup.TrySetResult();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DoCoreAsync();
                await WaitForNextIterationAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error($"[{Context.DeviceName}] {TaskName} 执行失败：{ex.Message}");
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
                    Logger.Error($"[{Context.DeviceName}] {TaskName} 重试等待失败：{retryException.Message}");
                }
            }
        }

        Logger.Info($"[{Context.DeviceName}] {TaskName} 已停止。");
    }
}
