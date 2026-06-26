using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Application.Features.Production.Planning;

public sealed class ProductionPlanSelectionServiceResolver(
    IEnumerable<IProductionPlanSelectionService> services,
    IEnumerable<IEdgeProcessModule> modules)
    : IProductionPlanSelectionServiceResolver
{
    private readonly IReadOnlyList<IProductionPlanSelectionService> _services = services.ToArray();
    private readonly IReadOnlyList<IEdgeProcessModule> _modules = modules.ToArray();

    public IProductionPlanSelectionService? ResolveCurrent()
    {
        if (_services.Count == 0)
        {
            return null;
        }

        foreach (var module in _modules)
        {
            var service = _services.FirstOrDefault(candidate =>
                Matches(candidate.ProcessType, module.ProcessType)
                || Matches(candidate.ProcessType, module.ModuleId));
            if (service is not null)
            {
                return service;
            }
        }

        return _services.Count == 1 ? _services[0] : null;
    }

    private static bool Matches(string left, string right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
