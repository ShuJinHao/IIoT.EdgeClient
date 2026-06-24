using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Features.Production.Planning;

namespace IIoT.Edge.Module.DieCutting.Mes;

/// <summary>
/// 模切主批计划选择服务，负责从 MES 加载主批计划并生成当前工序的追溯批次号。
/// </summary>
public sealed class DieCuttingProductionPlanService(
    DieCuttingModuleDefinition definition,
    DieCuttingMesChannel mesChannel,
    IModuleParamRoleProvider moduleParamRoleProvider,
    IProductionTimeProvider productionTime)
    : IProductionPlanSelectionService
{
    private readonly DieCuttingModuleDefinition _definition = definition;
    private readonly DieCuttingMesChannel _mesChannel = mesChannel;
    private readonly IModuleParamRoleProvider _moduleParamRoleProvider = moduleParamRoleProvider;
    private readonly IProductionTimeProvider _productionTime = productionTime;
    private ProductionPlanOption? _currentPlan;
    private string? _traceBatchNumber;
    private DateTime? _traceBatchGeneratedAt;
    private string? _traceBatchError;

    public string ProcessType => _definition.ProcessType;

    public ProductionPlanOption? CurrentPlan => _currentPlan;

    public async Task<ProductionPlanSelectionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var mesEnabled = await IsMesEnabledAsync(cancellationToken).ConfigureAwait(false);
        if (!mesEnabled)
        {
            return new ProductionPlanSelectionState(false, false, _currentPlan, string.Empty);
        }

        var upperComputerNo = await GetUpperComputerNoAsync(cancellationToken).ConfigureAwait(false);
        return new ProductionPlanSelectionState(
            true,
            true,
            _currentPlan,
            string.IsNullOrWhiteSpace(upperComputerNo) ? ProductionPlanSelectionErrorCodes.MissingUpperComputerNo : string.Empty,
            _traceBatchNumber,
            _traceBatchGeneratedAt,
            _traceBatchError);
    }

    public async Task<IReadOnlyList<ProductionPlanOption>> LoadPlansAsync(CancellationToken cancellationToken = default)
    {
        var mesEnabled = await IsMesEnabledAsync(cancellationToken).ConfigureAwait(false);
        if (!mesEnabled)
        {
            return [];
        }

        var upperComputerNo = await GetUpperComputerNoAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(upperComputerNo))
        {
            throw new InvalidOperationException(ProductionPlanSelectionErrorCodes.MissingUpperComputerNo);
        }

        var request = new DieCuttingMainPlanRequest(upperComputerNo, _productionTime.BusinessNow);
        var result = await _mesChannel.GetMainPlanAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Outcome != MesCallOutcome.Success)
        {
            throw new InvalidOperationException(result.Message);
        }

        return result.Data?.Orders.Select(MapPlan).ToList() ?? [];
    }

    public async Task SelectPlanAsync(ProductionPlanOption option, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        _currentPlan = option;
        _traceBatchNumber = null;
        _traceBatchGeneratedAt = null;
        _traceBatchError = null;

        if (string.IsNullOrWhiteSpace(option.MainPlanCode))
        {
            _traceBatchError = ProductionPlanSelectionErrorCodes.MissingMainPlanCode;
            throw new InvalidOperationException(_traceBatchError);
        }

        var operationCode = await GetOperationCodeAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(operationCode))
        {
            _traceBatchError = ProductionPlanSelectionErrorCodes.MissingOperationCode;
            throw new InvalidOperationException(_traceBatchError);
        }

        var request = new DieCuttingTraceBatchRequest(option.MainPlanCode.Trim(), operationCode.Trim());
        MesCallResult<DieCuttingTraceBatchResult> result;
        try
        {
            result = await _mesChannel.GenerateTraceBatchNumberAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _traceBatchError = ProductionPlanSelectionErrorCodes.TraceBatchTimeout;
            throw new InvalidOperationException(_traceBatchError);
        }

        if (result.Outcome != MesCallOutcome.Success)
        {
            _traceBatchError = ResolveTraceBatchError(result.Message);
            throw new InvalidOperationException(_traceBatchError);
        }

        var batchNumber = result.Data?.BatchNumber;
        if (string.IsNullOrWhiteSpace(batchNumber))
        {
            _traceBatchError = ProductionPlanSelectionErrorCodes.TraceBatchNumberMissing;
            throw new InvalidOperationException(_traceBatchError);
        }

        _traceBatchNumber = batchNumber.Trim();
        _traceBatchGeneratedAt = _productionTime.BusinessNow;
        _traceBatchError = null;
    }

    private static string ResolveTraceBatchError(string? message)
    {
        if (IsTimeoutMessage(message))
        {
            return ProductionPlanSelectionErrorCodes.TraceBatchTimeout;
        }

        return string.IsNullOrWhiteSpace(message)
            ? ProductionPlanSelectionErrorCodes.TraceBatchNumberMissing
            : message;
    }

    private static bool IsTimeoutMessage(string? message)
        => !string.IsNullOrWhiteSpace(message)
            && (message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || message.Contains("超时", StringComparison.OrdinalIgnoreCase));

    private Task<bool> IsMesEnabledAsync(CancellationToken cancellationToken)
        => _moduleParamRoleProvider.GetMesBoolAsync(
            _definition.ProcessType,
            ModuleParamRole.MesEnabled,
            defaultValue: true,
            cancellationToken);

    private async Task<string?> GetUpperComputerNoAsync(CancellationToken cancellationToken)
    {
        var value = await _moduleParamRoleProvider.GetMesStringAsync(
                _definition.ProcessType,
                ModuleParamRole.MesUpperComputerNo,
                defaultValue: _definition.UpperComputerNo,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(value) ? _definition.UpperComputerNo : value.Trim();
    }

    private async Task<string?> GetOperationCodeAsync(CancellationToken cancellationToken)
    {
        var value = await _moduleParamRoleProvider.GetMesStringAsync(
                _definition.ProcessType,
                ModuleParamRole.MesOperationCode,
                defaultValue: _definition.OperationCode,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(value) ? _definition.OperationCode : value.Trim();
    }

    private static ProductionPlanOption MapPlan(IReadOnlyList<DieCuttingMesField> fields)
    {
        var values = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Code))
            .GroupBy(field => field.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        string Get(string code) => values.TryGetValue(code, out var value) ? value : string.Empty;

        return new ProductionPlanOption(
            Id: Get("id"),
            MainPlanCode: Get("orderNo"),
            WorkOrderCode: Get("planOrderCode"),
            ErpOrderCode: Get("erpOrder"),
            ProductCode: Get("productCode"),
            ProductName: Get("productName"),
            PlanStatus: Get("planStatus"),
            ProcessCode: Get("pathCode"),
            ProcessName: Get("pathName"),
            LineCode: Get("productLineCode"),
            LineName: Get("productLine"),
            PlannedQuantity: Get("orderNum"),
            CompletedQuantity: Get("completedNum"),
            Unit: Get("unit"),
            ProductModel: Get("productModel"),
            StartTime: Get("startTime"),
            EndTime: Get("endTime"),
            Fields: values);
    }
}
