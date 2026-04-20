using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Runtime.Context;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Runtime.Base;

public abstract class PlcTaskBase : IPlcTask
{
    protected readonly IPlcBuffer Buffer;
    protected readonly ProductionContext Context;
    protected readonly ILogService Logger;
    protected CancellationToken TaskCancellationToken { get; private set; }

    public abstract string TaskName { get; }

    protected virtual int TaskLoopInterval => 10;
    protected virtual int ErrorRetryInterval => 1000;

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
        SetTaskCancellationToken(ct);
        Logger.Info($"[{Context.DeviceName}] {TaskName} started. Current step: {Step}");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DoCoreAsync();
                await Task.Delay(TaskLoopInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error($"[{Context.DeviceName}] {TaskName} failed: {ex.Message}");
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

        Logger.Info($"[{Context.DeviceName}] {TaskName} stopped.");
    }
}
