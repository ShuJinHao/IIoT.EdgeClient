using IIoT.Edge.Application.Features.Production.Planning;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public interface IProductionPlanSelectionPopupService
{
    Task<ProductionPlanOption?> ShowAsync(CancellationToken cancellationToken = default);
}
