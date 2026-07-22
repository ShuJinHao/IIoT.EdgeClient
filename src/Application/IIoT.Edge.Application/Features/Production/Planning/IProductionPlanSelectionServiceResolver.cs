using IIoT.Edge.Module.Contracts.Production;

namespace IIoT.Edge.Application.Features.Production.Planning;

public interface IProductionPlanSelectionServiceResolver
{
    IProductionPlanSelectionService? ResolveCurrent();
}
