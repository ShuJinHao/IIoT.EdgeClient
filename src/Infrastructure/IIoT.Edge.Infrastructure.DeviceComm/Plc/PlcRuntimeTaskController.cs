using IIoT.Edge.Application.Common.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcRuntimeTaskController(
    PlcRuntimeRegistry runtimeRegistry,
    IPlcTaskRuntimeStatusWriter? taskStatusWriter = null)
{
    public async Task RegisterPlanAsync(
        PlcRuntimeTaskPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        using var mutation = await runtimeRegistry
            .EnterRuntimeMutationAsync(plan.NetworkDeviceId, cancellationToken)
            .ConfigureAwait(false);
        SynchronizeWaitingForRuntime(plan);
        runtimeRegistry.RegisterTaskPlan(plan);
    }

    public async Task<PlcRuntimeTaskApplyResult> RegisterAndApplyAsync(
        PlcRuntimeTaskPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        using var mutation = await runtimeRegistry
            .EnterRuntimeMutationAsync(plan.NetworkDeviceId, cancellationToken)
            .ConfigureAwait(false);

        var runtime = runtimeRegistry.GetRuntime(plan.NetworkDeviceId);
        if (runtime is null)
        {
            SynchronizeWaitingForRuntime(plan);
            runtimeRegistry.RegisterTaskPlan(plan);
            return new PlcRuntimeTaskApplyResult(
                PlcRuntimeTaskApplyState.WaitingForRuntime,
                plan.TaskKeys);
        }

        var result = await runtime
            .ApplyTaskPlanAsync(plan, cancellationToken)
            .ConfigureAwait(false);
        runtimeRegistry.RegisterTaskPlan(plan);
        return result;
    }

    private void SynchronizeWaitingForRuntime(PlcRuntimeTaskPlan plan)
    {
        if (taskStatusWriter is null)
        {
            return;
        }

        var previousKeys = runtimeRegistry
            .GetTaskPlan(plan.NetworkDeviceId, plan.PlcCode, plan.DeviceName)
            .TaskKeys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextKeys = plan.TaskKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in previousKeys.Except(nextKeys, StringComparer.OrdinalIgnoreCase))
        {
            taskStatusWriter.Remove(plan.PlcCode, removed);
        }

        foreach (var taskKey in nextKeys)
        {
            taskStatusWriter.SetState(
                plan.PlcCode,
                taskKey,
                PlcTaskRuntimeState.WaitingForRuntime);
        }
    }
}
