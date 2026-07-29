namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcRuntimeTaskController(PlcRuntimeRegistry runtimeRegistry)
{
    public async Task RegisterPlanAsync(
        PlcRuntimeTaskPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        using var mutation = await runtimeRegistry
            .EnterRuntimeMutationAsync(plan.NetworkDeviceId, cancellationToken)
            .ConfigureAwait(false);
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
}
