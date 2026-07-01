using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Application.Modules.Mes;
using IIoT.Edge.Module.DieCutting.Config.Parameters;
using IIoT.Edge.Module.DieCutting.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.Application.Abstractions.Time;

namespace IIoT.Edge.Module.DieCutting.Mes;

/// <summary>
/// 模切 MES 通道，负责把采样快照转换成 MES 字段 payload。
/// </summary>
public sealed class DieCuttingMesChannel
    : MesScenarioChannelBase<DieCuttingCellData>, IDieCuttingMesScenarioChannel
{
    private const string EmptyField = "";
    private readonly DieCuttingModuleDefinition _definition;
    private readonly IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> _parameters;

    public DieCuttingMesChannel(
        DieCuttingModuleDefinition definition,
        MesRequestExecutor requestExecutor,
        IModuleParamRoleProvider moduleParamRoleProvider,
        IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters,
        ILogService logger,
        IProductionTimeProvider productionTime)
        : base(definition.ProcessType, logger, requestExecutor, moduleParamRoleProvider, productionTime)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        _parameters = parameters;
    }

    /// <summary>
    /// 获取 MES 主批计划。
    /// </summary>
    public async Task<MesCallResult<DieCuttingMainPlan>> GetMainPlanAsync(
        DieCuttingMainPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UpperComputerNo))
        {
            return MesCallResult<DieCuttingMainPlan>.InvalidContext("上位机编码不能为空。");
        }

        var query = new Dictionary<string, string?>
        {
            ["upperComputerNo"] = request.UpperComputerNo.Trim(),
            ["timestamp"] = FormatTimestamp(request.Timestamp)
        };

        return await ExecuteOptionalMesGetAsync(
                "主批计划",
                ct => GetMesPathAsync(DieCuttingParams.Mes.OrderPath, ct),
                query,
                DieCuttingMesResponseParser.ParseMainPlan,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 按主批计划和工序编码生成 MES 追溯批次号。
    /// </summary>
    public async Task<MesCallResult<DieCuttingTraceBatchResult>> GenerateTraceBatchNumberAsync(
        DieCuttingTraceBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.MasterPlanCode))
        {
            return MesCallResult<DieCuttingTraceBatchResult>.InvalidContext("主批次号不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.OperationCode))
        {
            return MesCallResult<DieCuttingTraceBatchResult>.InvalidContext("工序编码不能为空。");
        }

        var payload = new
        {
            masterPlanCode = request.MasterPlanCode.Trim(),
            operationCode = request.OperationCode.Trim()
        };

        return await ExecuteOptionalMesPostAsync(
                "追溯批次号",
                ct => GetMesPathAsync(DieCuttingParams.Mes.BatchNumberPath, ct),
                payload,
                DieCuttingMesResponseParser.ParseTraceBatch,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 上传模切当前追溯快照，空字段按 MES 确认传空字符串。
    /// </summary>
    public async Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        DieCuttingRealtimeSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return await ExecuteRequiredMesAsync(
            "模切追溯出站",
            ct => GetMesPathAsync(DieCuttingParams.Mes.OutboundPath, ct),
            device,
            envelope => new
            {
                upperComputerNo = envelope.UpperComputerNo,
                timestamp = envelope.Timestamp,
                sign = envelope.Sign,
                stationNo = envelope.StationNo,
                outboundTime = FormatTimestamp(snapshot.WindowCompleteAt),
                batchNumber = snapshot.PunchingLotNumber,
                serialNumber = EmptyField,
                operationCode = _definition.OperationCode,
                data = new
                {
                    produce = BuildPunchingProduce(snapshot)
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MesCallResult> UploadEquipmentStatusAsync(
        DeviceSession? device,
        DieCuttingDeviceStatusSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return await ExecuteOptionalMesAsync(
            "模切设备状态",
            ct => GetMesPathAsync(DieCuttingParams.Mes.EquipmentStatusPath, ct),
            device,
            envelope => CreateStandardMesPayload(
                envelope,
                new
                {
                    devices = new[]
                    {
                        new
                        {
                            stationNo = envelope.StationNo,
                            status = snapshot.StatusCode,
                            msg = snapshot.Messages
                        }
                    }
                }),
            cancellationToken).ConfigureAwait(false);
    }

    protected override Task<MesCallResult> UploadOutboundRecordAsync(
        DeviceSession? device,
        DieCuttingCellData cellData,
        CellCompletedRecord record,
        CancellationToken cancellationToken)
        => Task.FromResult(MesCallResult.Disabled("模切首版为采样上传，不走 DataPipeline 出料补传。"));

    private IReadOnlyList<object> BuildPunchingProduce(DieCuttingRealtimeSnapshot snapshot)
        =>
        [
            CreateProduceItem("punchingLotNumber", "批次号", snapshot.PunchingLotNumber),
            CreateProduceItem("clipNo", "弹夹号", snapshot.ClipNo),
            CreateProduceItem("punchingDeviceCode", "设备编码", snapshot.PunchingDeviceCode),
            CreateProduceItem("punchingDeviceName", "设备名称", snapshot.PunchingDeviceName),
            CreateProduceItem("punchingQuantity", "产量", snapshot.PunchingQuantity),
            CreateProduceItem("punchingUom", "单位", snapshot.PunchingUom),
            CreateProduceItem("punchingStartTime", "开始时间", FormatTimestamp(snapshot.WindowStartAt)),
            CreateProduceItem("punchingCompleteTime", "结束时间", FormatTimestamp(snapshot.WindowCompleteAt)),
            CreateProduceItem("slittingConsumeQuantity", "分切消耗量", EmptyField),
            CreateProduceItem("punchingSpeed", "模切速度", snapshot.PunchingSpeed),
            CreateProduceItem("polePieceLength", "极片长度", snapshot.PlateLengthMm),
            CreateProduceItem("polePieceWidth", "极片宽度", snapshot.PlateWidthMm),
            CreateProduceItem("collectorLength", "集流体长度", EmptyField),
            CreateProduceItem("collectorWidth", "集流体宽度", EmptyField),
            CreateProduceItem("collectorLongMargin", "集流体长边距", EmptyField),
            CreateProduceItem("collectorShortMargin", "集流体短边距", EmptyField),
            CreateProduceItem("collectorFullInspectionSurface", "集流体全检面", EmptyField),
            CreateProduceItem("collectorNoFullInspectionSurface", "集流体非全检面", EmptyField),
            CreateProduceItem("collectorWhiteMaterial", "集流体白料", EmptyField),
            CreateProduceItem("polePieceWeight", "极片重量", EmptyField),
            CreateProduceItem("transverseBurr", "横向毛刺", EmptyField),
            CreateProduceItem("longitudinalBurr", "纵向毛刺", EmptyField),
            CreateProduceItem("transverseBurrs", "横向毛边", EmptyField),
            CreateProduceItem("longitudinalBurrs", "纵向毛边", EmptyField),
            CreateProduceItem("receivingSheetAlignment", "收料对齐度", EmptyField),
            CreateProduceItem("punchingAppearance", "外观", EmptyField)
        ];

    private static object CreateProduceItem(string code, string name, object? value)
        => new
        {
            code,
            name,
            val = value?.ToString() ?? EmptyField
        };

    private async Task<string?> GetMesPathAsync(
        DieCuttingParams.Mes pathKey,
        CancellationToken cancellationToken)
    {
        var snapshot = await _parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Mes<string>(pathKey);
    }
}

/// <summary>
/// 模切 MES 场景通道契约，运行任务只依赖本插件强类型接口。
/// </summary>
public interface IDieCuttingMesScenarioChannel : IProcessMesUploader
{
    Task<MesCallResult<DieCuttingMainPlan>> GetMainPlanAsync(
        DieCuttingMainPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<MesCallResult<DieCuttingTraceBatchResult>> GenerateTraceBatchNumberAsync(
        DieCuttingTraceBatchRequest request,
        CancellationToken cancellationToken = default);

    Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        DieCuttingRealtimeSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<MesCallResult> UploadEquipmentStatusAsync(
        DeviceSession? device,
        DieCuttingDeviceStatusSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
