using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IStationRuntimeFactory
{
    string ModuleId { get; }

    List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context);
}

public interface IStationRuntimeRegistry
{
    void Register(IStationRuntimeFactory factory);

    bool HasFactory(string moduleId);

    bool TryGetFactory(string moduleId, out IStationRuntimeFactory factory);

    IReadOnlyDictionary<string, IStationRuntimeFactory> GetRegistrations();
}
