using IIoT.Edge.Module.Contracts.Production;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public interface IProductionPlanSelectionPopupService
{
    Task<ProductionPlanOption?> ShowAsync(CancellationToken cancellationToken = default);
}
