using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Runtime.Stations.Injection;

/// <summary>
/// v1 baseline for Injection.
/// The module owns the runtime entry now, but there are currently no
/// confirmed injection-specific PLC tasks beyond SignalInteraction.
/// </summary>
public sealed class InjectionStationRuntimeFactory : IStationRuntimeFactory
{
    public string ModuleId => "Injection";

    public List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(context);

        return [];
    }
}
