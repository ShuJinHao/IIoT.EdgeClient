using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IProductionContextSignalBindingStore
{
    void Set(ProductionContext context, IReadOnlyCollection<ModuleIoSnapshot> bindings);

    IReadOnlyList<ModuleIoSnapshot> Get(ProductionContext context);
}
