using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Production.Planning;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class ProductionPlanSelectionServiceResolverBehaviorTests
{
    [Fact]
    public void ResolveCurrent_WhenMultipleServicesExist_ShouldMatchEnabledModuleProcessType()
    {
        var anodeService = new StubProductionPlanSelectionService("DieCuttingAnodeLine");
        var cathodeService = new StubProductionPlanSelectionService("DieCuttingCathodeLine");
        var resolver = new ProductionPlanSelectionServiceResolver(
            [anodeService, cathodeService],
            [new StubEdgeProcessModule("DieCuttingCathodeLine")]);

        var resolved = resolver.ResolveCurrent();

        Assert.Same(cathodeService, resolved);
    }

    [Fact]
    public void ResolveCurrent_WhenOnlyOneServiceExists_ShouldFallbackToSingleService()
    {
        var service = new StubProductionPlanSelectionService("Homogenization");
        var resolver = new ProductionPlanSelectionServiceResolver(
            [service],
            []);

        var resolved = resolver.ResolveCurrent();

        Assert.Same(service, resolved);
    }

    [Fact]
    public void ResolveCurrent_WhenMultipleServicesDoNotMatchModule_ShouldReturnNull()
    {
        var resolver = new ProductionPlanSelectionServiceResolver(
            [
                new StubProductionPlanSelectionService("DieCuttingAnodeLine"),
                new StubProductionPlanSelectionService("DieCuttingCathodeLine")
            ],
            [new StubEdgeProcessModule("Homogenization")]);

        var resolved = resolver.ResolveCurrent();

        Assert.Null(resolved);
    }

    private sealed class StubProductionPlanSelectionService(string processType) : IProductionPlanSelectionService
    {
        public string ProcessType { get; } = processType;

        public ProductionPlanOption? CurrentPlan => null;

        public Task<ProductionPlanSelectionState> GetStateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ProductionPlanSelectionState(false, false, null, string.Empty));

        public Task<IReadOnlyList<ProductionPlanOption>> LoadPlansAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProductionPlanOption>>([]);

        public Task SelectPlanAsync(ProductionPlanOption option, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubEdgeProcessModule(string moduleId) : IEdgeProcessModule
    {
        public string ModuleId { get; } = moduleId;

        public string ProcessType => ModuleId;

        public string DisplayName => ModuleId;

        public void Configure(IEdgeProcessModuleBuilder builder)
        {
        }
    }
}
