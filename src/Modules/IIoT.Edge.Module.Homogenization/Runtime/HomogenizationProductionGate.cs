using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Production.Planning;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 匀浆生产门禁。MES 未启用时不拦截；MES 启用时必须已有主批计划和追溯批次号。
/// </summary>
public sealed class HomogenizationProductionGate(IProductionPlanSelectionService planSelectionService)
    : IHomogenizationProductionGate
{
    private readonly IProductionPlanSelectionService _planSelectionService = planSelectionService;

    public async Task<MesCallResult> EnsureReadyAsync(
        HomogenizationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = await _planSelectionService.GetStateAsync(cancellationToken).ConfigureAwait(false);
        context.SelectedProductionPlan = state.CurrentPlan;
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
