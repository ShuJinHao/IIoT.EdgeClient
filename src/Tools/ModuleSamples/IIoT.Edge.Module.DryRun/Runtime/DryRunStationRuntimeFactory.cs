using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.DryRun.Constants;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Module.DryRun.Runtime;

public sealed class DryRunStationRuntimeFactory : IStationRuntimeFactory
{
    public string ModuleId => DryRunModuleConstants.ModuleId;

    public List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context)
        => [];
}
