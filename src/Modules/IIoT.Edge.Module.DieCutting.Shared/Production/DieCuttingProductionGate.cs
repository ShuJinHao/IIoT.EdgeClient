using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Module.DieCutting.Mes;

namespace IIoT.Edge.Module.DieCutting.Production;

/// <summary>
/// 模切生产门禁。MES 未启用时不拦截；MES 启用时必须已有本次启动选择的主批计划和追溯批次号。
/// </summary>
internal interface IDieCuttingProductionGate
{
    Task<MesCallResult> EnsureReadyAsync(
        DieCuttingContext context,
        CancellationToken cancellationToken = default);
}

internal sealed class DieCuttingProductionGate(DieCuttingProductionPlanService planSelectionService)
    : IDieCuttingProductionGate
{
    private readonly DieCuttingProductionPlanService _planSelectionService = planSelectionService;

    public async Task<MesCallResult> EnsureReadyAsync(
        DieCuttingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = await _planSelectionService.GetStateAsync(cancellationToken).ConfigureAwait(false);
        context.SelectedProductionPlan = state.CurrentPlan;
        context.PlanSessionId = state.PlanSessionId;
        context.TraceBatchNumber = state.TraceBatchNumber;
        context.TraceBatchGeneratedAt = state.TraceBatchGeneratedAt;
        context.TraceBatchError = state.TraceBatchError;

        if (!state.IsMesEnabled || !state.RequiresSelection)
        {
            return MesCallResult.Success("MES 未启用主批计划门禁。");
        }

        if (!state.HasSelectedPlan)
        {
            return MesCallResult.BusinessRejected("MES 已启用，请先选择主批计划。");
        }

        if (!state.HasTraceBatchNumber)
        {
            var message = string.IsNullOrWhiteSpace(state.TraceBatchError)
                ? "MES 已启用，请先生成追溯批次号。"
                : state.TraceBatchError;
            return MesCallResult.BusinessRejected(message);
        }

        return MesCallResult.Success("MES 生产前置条件已满足。");
    }
}
