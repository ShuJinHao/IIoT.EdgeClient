namespace IIoT.Edge.Application.Features.Production.Planning;

public sealed record ProductionPlanSelectionState(
    bool IsMesEnabled,
    bool RequiresSelection,
    ProductionPlanOption? CurrentPlan,
    string Message,
    string? TraceBatchNumber = null,
    DateTime? TraceBatchGeneratedAt = null,
    string? TraceBatchError = null,
    string? PlanSessionId = null)
{
    public bool HasSelectedPlan => CurrentPlan is not null;

    public bool HasTraceBatchNumber => !string.IsNullOrWhiteSpace(TraceBatchNumber);
}
