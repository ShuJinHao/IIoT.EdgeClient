using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Module.Contracts.Logging;

namespace IIoT.Edge.Shell.Core;

public interface IPlcTaskBindingRuntimeTransaction
{
    PlcRuntimeTaskPlan Capture(int networkDeviceId, string deviceName);

    Task<PlcRuntimeTaskApplyResult> ApplyCurrentBindingsAsync(
        int networkDeviceId,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(
        PlcRuntimeTaskPlan snapshot,
        CancellationToken cancellationToken = default);
}

public sealed class PlcTaskBindingRuntimeTransaction(
    PlcRuntimeRegistry runtimeRegistry,
    PlcRuntimeTaskController runtimeTaskController,
    IPlcRuntimeTaskBinder runtimeTaskBinder)
    : IPlcTaskBindingRuntimeTransaction
{
    public PlcRuntimeTaskPlan Capture(int networkDeviceId, string deviceName)
        => runtimeRegistry.GetTaskPlan(networkDeviceId, deviceName);

    public Task<PlcRuntimeTaskApplyResult> ApplyCurrentBindingsAsync(
        int networkDeviceId,
        CancellationToken cancellationToken = default)
        => runtimeTaskBinder.BindDeviceAsync(
            networkDeviceId,
            applyToRunningDevice: true,
            cancellationToken);

    public async Task RestoreAsync(
        PlcRuntimeTaskPlan snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await runtimeTaskController
            .RegisterAndApplyAsync(snapshot, cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class PlcTaskBindingTransactionService(
    IPlcTaskBindingPersistenceTransaction persistenceTransaction,
    IPlcTaskBindingRuntimeTransaction runtimeTransaction,
    ILogService logger)
    : IPlcTaskBindingTransactionService
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _deviceGates = new();

    public async Task<PlcTaskBindingSaveApplyResult> SaveAndApplyAsync(
        int networkDeviceId,
        string moduleId,
        IReadOnlyDictionary<string, bool> taskStates,
        CancellationToken cancellationToken = default)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException("网络设备 Id 必须大于 0。", nameof(networkDeviceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(taskStates);

        var gate = _deviceGates.GetOrAdd(
            networkDeviceId,
            static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var preparation = await persistenceTransaction
                .PrepareAsync(networkDeviceId, moduleId, taskStates, cancellationToken)
                .ConfigureAwait(false);
            var runtimeSnapshot = runtimeTransaction.Capture(
                preparation.NetworkDeviceId,
                preparation.DeviceName);

            await persistenceTransaction
                .CommitAsync(preparation, cancellationToken)
                .ConfigureAwait(false);

            PlcRuntimeTaskApplyResult runtimeResult;
            PlcTaskBindingSaveApplyState resultState;
            try
            {
                runtimeResult = await runtimeTransaction
                    .ApplyCurrentBindingsAsync(networkDeviceId, cancellationToken)
                    .ConfigureAwait(false);
                resultState = MapState(runtimeResult.State);
            }
            catch (Exception primaryFailure)
            {
                await RollBackAsync(
                        preparation,
                        runtimeSnapshot,
                        primaryFailure)
                    .ConfigureAwait(false);
                throw;
            }

            LogSuccessBestEffort(preparation, runtimeResult);
            return new PlcTaskBindingSaveApplyResult(
                resultState,
                runtimeResult.EnabledTaskKeys
                    .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RollBackAsync(
        PlcTaskBindingSavePreparation preparation,
        PlcRuntimeTaskPlan runtimeSnapshot,
        Exception primaryFailure)
    {
        var rollbackFailures = new List<Exception>();
        try
        {
            await runtimeTransaction
                .RestoreAsync(runtimeSnapshot, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            rollbackFailures.Add(
                new InvalidOperationException("PLC 原运行任务组合回滚失败。", exception));
        }

        try
        {
            await persistenceTransaction
                .RestoreAsync(preparation, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            rollbackFailures.Add(
                new InvalidOperationException("PLC 原 SQLite 任务绑定回滚失败。", exception));
        }

        if (rollbackFailures.Count > 0)
        {
            throw new PlcTaskBindingTransactionException(
                primaryFailure,
                rollbackFailures);
        }

        ExceptionDispatchInfo.Capture(primaryFailure).Throw();
    }

    private void LogSuccessBestEffort(
        PlcTaskBindingSavePreparation preparation,
        PlcRuntimeTaskApplyResult runtimeResult)
    {
        try
        {
            if (preparation.DisabledHeartbeatTaskNames.Count > 0)
            {
                logger.Warn(
                    $"PLC“{preparation.DeviceName}”已关闭心跳类任务：{string.Join("、", preparation.DisabledHeartbeatTaskNames)}。");
            }

            var stateText = runtimeResult.State switch
            {
                PlcRuntimeTaskApplyState.Applied => "已按 TaskKey 增量应用",
                PlcRuntimeTaskApplyState.WaitingForConnection => "已保存，等待 PLC 连接后应用",
                PlcRuntimeTaskApplyState.WaitingForRuntime => "已保存，等待 PLC runtime 启动后应用",
                _ => "已保存"
            };
            logger.Info(
                $"[{preparation.DeviceName}] PLC 任务绑定{stateText}：{string.Join("、", runtimeResult.EnabledTaskKeys)}。");
        }
        catch
        {
            // 日志订阅者不得把已经成功提交且应用的事务反写为业务失败。
        }
    }

    private static PlcTaskBindingSaveApplyState MapState(PlcRuntimeTaskApplyState state)
        => state switch
        {
            PlcRuntimeTaskApplyState.Applied => PlcTaskBindingSaveApplyState.Applied,
            PlcRuntimeTaskApplyState.WaitingForConnection => PlcTaskBindingSaveApplyState.WaitingForConnection,
            PlcRuntimeTaskApplyState.WaitingForRuntime => PlcTaskBindingSaveApplyState.WaitingForRuntime,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
}
