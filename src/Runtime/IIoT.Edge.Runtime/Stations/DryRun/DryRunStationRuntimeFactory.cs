using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Modules.DryRun;

namespace IIoT.Edge.Runtime.Stations.DryRun;

public sealed class DryRunStationRuntimeFactory : IStationRuntimeFactory
{
    public string ModuleId => DryRunModuleConstants.ModuleId;

    public List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context)
        => [];
}
