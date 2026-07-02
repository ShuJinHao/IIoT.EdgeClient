using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Application.Modules.Mes;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Module.Homogenization.Mes;

/// <summary>
/// 匀浆 MES 通道实现。通用签名、工站和请求执行由 Application 基类处理，本类只保留匀浆字段映射和 MES code 选择。
/// </summary>
public sealed class HomogenizationMesChannel
    : MesScenarioChannelBase<HomogenizationCellData>, IHomogenizationMesScenarioChannel
{
    private readonly HomogenizationMesPayloadBuilder _payloadBuilder;
    private readonly IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> _parameters;

    public HomogenizationMesChannel(
        MesRequestExecutor requestExecutor,
        IModuleParamRoleProvider moduleParamRoleProvider,
        IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> parameters,
        ILogService logger,
        IProductionTimeProvider productionTime,
        HomogenizationMesPayloadBuilder payloadBuilder)
        : base(DependencyInjection.ModuleKey, logger, requestExecutor, moduleParamRoleProvider, productionTime)
    {
        _parameters = parameters;
        _payloadBuilder = payloadBuilder;
    }

    public async Task<MesCallResult<HomogenizationMainPlan>> GetMainPlanAsync(
        HomogenizationMainPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UpperComputerNo))
        {
            return MesCallResult<HomogenizationMainPlan>.InvalidContext("上位机编码不能为空。");
        }

        var query = new Dictionary<string, string?>
        {
            ["upperComputerNo"] = request.UpperComputerNo.Trim(),
            ["timestamp"] = FormatTimestamp(request.Timestamp)
        };

        return await ExecuteOptionalMesGetAsync(
                "主批计划",
                ct => GetMesPathAsync(HomogenizationParams.Mes.OrderPath, ct),
                query,
                HomogenizationMesResponseParser.ParseMainPlan,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MesCallResult<HomogenizationTraceBatchResult>> GenerateTraceBatchNumberAsync(
        HomogenizationTraceBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.MasterPlanCode))
        {
            return MesCallResult<HomogenizationTraceBatchResult>.InvalidContext("主批次号不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.OperationCode))
        {
            return MesCallResult<HomogenizationTraceBatchResult>.InvalidContext("工序编码不能为空。");
        }

        var payload = new
        {
            masterPlanCode = request.MasterPlanCode.Trim(),
            operationCode = request.OperationCode.Trim()
        };

        return await ExecuteOptionalMesPostAsync(
                "追溯批次号",
                ct => GetMesPathAsync(HomogenizationParams.Mes.BatchNumberPath, ct),
                payload,
                HomogenizationMesResponseParser.ParseTraceBatch,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 进站校验只需要托盘码；托盘码如何读取、何时触发仍由匀浆 PLC 任务负责。
    /// </summary>
    public async Task<MesCallResult> UploadInboundAsync(
        DeviceSession? device,
        string trayCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trayCode))
        {
            return MesCallResult.InvalidContext("托盘码不能为空。");
        }

        return await ExecuteOptionalMesAsync(
            "进站",
            ct => GetMesPathAsync(HomogenizationParams.Mes.InboundPath, ct),
            device,
            // 进站接口沿用 MES 文档中的托盘字段结构，不把这些匀浆业务字段上移到共享层。
            envelope => CreateStandardMesPayload(
                envelope,
                new
                {
                    stackTrayNo = trayCode,
                    weldTrayNo = trayCode,
                    productNo = trayCode,
                    devices = (object?)null,
                    boms = (object?)null
                }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 出料上传使用完整匀浆电芯数据构造 produce 列表，失败补偿时保存的也是这条电芯记录 JSON。
    /// </summary>
    public async Task<MesCallResult> UploadOutboundAsync(
        DeviceSession? device,
        HomogenizationCellData cellData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cellData);

        if (string.IsNullOrWhiteSpace(cellData.TrayCode))
        {
            return MesCallResult.InvalidContext("出料托盘码不能为空。");
        }

        return await ExecuteRequiredMesAsync(
            "出料",
            ct => GetMesPathAsync(HomogenizationParams.Mes.OutboundPath, ct),
            device,
            // 出料 payload 字段来自 HomogenizationCellData 和匀浆 MES code 配置。
            envelope => new
            {
                upperComputerNo = envelope.UpperComputerNo,
                timestamp = envelope.Timestamp,
                sign = envelope.Sign,
                stationNo = envelope.StationNo,
                outboundTime = FormatTimestamp(cellData.CompletedTime ?? ProductionTime.UtcNow),
                serialNumber = cellData.TrayCode,
                data = new
                {
                    boundNo = cellData.TrayCode,
                    lastBoundNo = cellData.TrayCode,
                    produce = _payloadBuilder.BuildOutboundProduce(cellData, FormatTimestamp)
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 实时上传把匀浆实时快照转换为 MES item 数组，字段 code 仍从插件配置读取。
    /// </summary>
    public async Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        HomogenizationRealtimeSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return await ExecuteOptionalMesAsync(
            "实时数据",
            ct => GetMesPathAsync(HomogenizationParams.Mes.RealtimePath, ct),
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
                            collectTime = FormatTimestamp(snapshot.CapturedAt),
                            data = _payloadBuilder.BuildRealtimeItems(snapshot)
                        }
                    }
                }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 配方上传把每个配方数组展开为带序号的 MES item，数组长度和字段含义由匀浆插件控制。
    /// </summary>
    public async Task<MesCallResult> UploadRecipeAsync(
        DeviceSession? device,
        HomogenizationRecipeSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return await ExecuteOptionalMesAsync(
            "配方",
            ct => GetMesPathAsync(HomogenizationParams.Mes.RecipePath, ct),
            device,
            envelope => CreateStandardMesPayload(
                envelope,
                new
                {
                    devices = _payloadBuilder.BuildRecipeItems(snapshot)
                }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 设备状态上传只封装匀浆状态快照，状态码到文本的解释保留在匀浆配置。
    /// </summary>
    public async Task<MesCallResult> UploadEquipmentStatusAsync(
        DeviceSession? device,
        HomogenizationEquipmentStatusSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return await ExecuteOptionalMesAsync(
            "设备状态",
            ct => GetMesPathAsync(HomogenizationParams.Mes.EquipmentStatusPath, ct),
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
        HomogenizationCellData cellData,
        CellCompletedRecord record,
        CancellationToken cancellationToken)
        => cellData.RecordKind switch
        {
            HomogenizationCellData.RecordKindInbound => UploadInboundAsync(
                device,
                cellData.TrayCode,
                cancellationToken),
            HomogenizationCellData.RecordKindRealtime => cellData.RealtimeSnapshot is null
                ? Task.FromResult(MesCallResult.InvalidContext("实时数据快照不能为空。"))
                : UploadRealtimeAsync(device, cellData.RealtimeSnapshot, cancellationToken),
            HomogenizationCellData.RecordKindRecipe => cellData.RecipeSnapshot is null
                ? Task.FromResult(MesCallResult.InvalidContext("配方快照不能为空。"))
                : UploadRecipeAsync(device, cellData.RecipeSnapshot, cancellationToken),
            HomogenizationCellData.RecordKindEquipmentStatus => cellData.EquipmentStatusSnapshot is null
                ? Task.FromResult(MesCallResult.InvalidContext("设备状态快照不能为空。"))
                : UploadEquipmentStatusAsync(device, cellData.EquipmentStatusSnapshot, cancellationToken),
            _ => UploadOutboundAsync(device, cellData, cancellationToken)
        };

    private async Task<string?> GetMesPathAsync(
        HomogenizationParams.Mes pathKey,
        CancellationToken cancellationToken)
    {
        var snapshot = await _parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Mes<string>(pathKey);
    }
}

/// <summary>
/// 匀浆 MES 场景通道契约。泛型实参只在插件边界声明一次，运行任务和测试依赖本插件强类型接口。
/// </summary>
public interface IHomogenizationMesScenarioChannel
    : IMesScenarioChannel<
        HomogenizationCellData,
        string,
        HomogenizationRealtimeSnapshot,
        HomogenizationRecipeSnapshot,
        HomogenizationEquipmentStatusSnapshot,
        HomogenizationMainPlanRequest,
        HomogenizationMainPlan,
        HomogenizationTraceBatchRequest,
        HomogenizationTraceBatchResult>
{
}
