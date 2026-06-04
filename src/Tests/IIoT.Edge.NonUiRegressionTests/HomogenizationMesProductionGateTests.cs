using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Module.Homogenization.Runtime;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationMesProductionGateTests
{
    [Fact]
    public async Task EnsureReadyAsync_WhenMesEnabledWithoutPlan_ShouldReject()
    {
        var gate = new HomogenizationProductionGate(new FakeProductionPlanSelectionService(
            new ProductionPlanSelectionState(
                IsMesEnabled: true,
                RequiresSelection: true,
                CurrentPlan: null,
                Message: string.Empty)));
        var context = new HomogenizationContext();

        var result = await gate.EnsureReadyAsync(context);

        Assert.Equal(MesCallOutcome.BusinessRejected, result.Outcome);
        Assert.Null(context.SelectedProductionPlan);
        Assert.Null(context.TraceBatchNumber);
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenPlanSelectedWithoutTraceBatch_ShouldReject()
    {
        var plan = CreatePlan();
        var gate = new HomogenizationProductionGate(new FakeProductionPlanSelectionService(
            new ProductionPlanSelectionState(
                IsMesEnabled: true,
                RequiresSelection: true,
                CurrentPlan: plan,
                Message: string.Empty,
                TraceBatchError: ProductionPlanSelectionErrorCodes.TraceBatchNumberMissing)));
        var context = new HomogenizationContext();

        var result = await gate.EnsureReadyAsync(context);

        Assert.Equal(MesCallOutcome.BusinessRejected, result.Outcome);
        Assert.Equal(plan, context.SelectedProductionPlan);
        Assert.Equal(ProductionPlanSelectionErrorCodes.TraceBatchNumberMissing, context.TraceBatchError);
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenPlanAndTraceBatchExist_ShouldPassAndWriteContext()
    {
        var plan = CreatePlan();
        var generatedAt = new DateTime(2026, 5, 22, 8, 30, 0);
        var gate = new HomogenizationProductionGate(new FakeProductionPlanSelectionService(
            new ProductionPlanSelectionState(
                IsMesEnabled: true,
                RequiresSelection: true,
                CurrentPlan: plan,
                Message: string.Empty,
                TraceBatchNumber: "TRACE-001",
                TraceBatchGeneratedAt: generatedAt)));
        var context = new HomogenizationContext();

        var result = await gate.EnsureReadyAsync(context);

        Assert.Equal(MesCallOutcome.Success, result.Outcome);
        Assert.Equal(plan, context.SelectedProductionPlan);
        Assert.Equal("TRACE-001", context.TraceBatchNumber);
        Assert.Equal(generatedAt, context.TraceBatchGeneratedAt);
        Assert.Null(context.TraceBatchError);
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenMesDisabled_ShouldPassWithoutPlan()
    {
        var gate = new HomogenizationProductionGate(new FakeProductionPlanSelectionService(
            new ProductionPlanSelectionState(
                IsMesEnabled: false,
                RequiresSelection: false,
                CurrentPlan: null,
                Message: string.Empty)));
        var context = new HomogenizationContext();

        var result = await gate.EnsureReadyAsync(context);

        Assert.Equal(MesCallOutcome.Success, result.Outcome);
    }

    private static ProductionPlanOption CreatePlan()
        => new(
            Id: "1",
            MainPlanCode: "PLAN-001",
            WorkOrderCode: "WO-001",
            ErpOrderCode: string.Empty,
            ProductCode: "P-001",
            ProductName: "Product",
            PlanStatus: "Issued",
            ProcessCode: "CG",
            ProcessName: "Process",
            LineCode: string.Empty,
            LineName: string.Empty,
            PlannedQuantity: "10",
            CompletedQuantity: string.Empty,
            Unit: string.Empty,
            ProductModel: string.Empty,
            StartTime: string.Empty,
            EndTime: string.Empty,
            Fields: new Dictionary<string, string>());

    private sealed class FakeProductionPlanSelectionService(ProductionPlanSelectionState state)
        : IProductionPlanSelectionService
    {
        public string ProcessType => "Homogenization";

        public ProductionPlanOption? CurrentPlan => state.CurrentPlan;

        public Task<ProductionPlanSelectionState> GetStateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(state);

        public Task<IReadOnlyList<ProductionPlanOption>> LoadPlansAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProductionPlanOption>>([]);

        public Task SelectPlanAsync(ProductionPlanOption option, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
