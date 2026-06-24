namespace IIoT.Edge.Application.Features.Production.Planning;

public interface IProductionPlanSelectionServiceResolver
{
    IProductionPlanSelectionService? ResolveCurrent();
}
