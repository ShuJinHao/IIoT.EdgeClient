namespace IIoT.Edge.Application.Features.Production.Planning;

public interface IProductionPlanSelectionService
{
    string ProcessType { get; }

    ProductionPlanOption? CurrentPlan { get; }

    Task<ProductionPlanSelectionState> GetStateAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionPlanOption>> LoadPlansAsync(CancellationToken cancellationToken = default);

    Task SelectPlanAsync(ProductionPlanOption option, CancellationToken cancellationToken = default);
}
