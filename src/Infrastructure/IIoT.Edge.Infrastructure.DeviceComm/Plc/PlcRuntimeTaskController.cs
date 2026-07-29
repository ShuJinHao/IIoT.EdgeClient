namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcRuntimeTaskController(PlcRuntimeRegistry runtimeRegistry)
{
    public void RegisterPlan(PlcRuntimeTaskPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        runtimeRegistry.RegisterTaskPlan(plan);
    }

    public async Task<PlcRuntimeTaskApplyResult> RegisterAndApplyAsync(
        PlcRuntimeTaskPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        runtimeRegistry.RegisterTaskPlan(plan);

        var runtime = runtimeRegistry.GetRuntime(plan.DeviceName);
        if (runtime is null)
        {
            return new PlcRuntimeTaskApplyResult(
                PlcRuntimeTaskApplyState.WaitingForRuntime,
                plan.TaskKeys);
        }

        return await runtime
            .ApplyTaskPlanAsync(plan, cancellationToken)
            .ConfigureAwait(false);
    }
}
