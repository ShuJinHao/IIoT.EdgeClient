using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Application.Modules.Mes;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Integration;

/// <summary>
/// 匀浆 MES 通道实现。通用签名、工站和请求执行由 Application 基类处理，本类只保留匀浆字段映射和 MES code 选择。
/// </summary>
public sealed class HomogenizationMesChannel
    : MesScenarioChannelBase<
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
    private readonly HomogenizationMesOptions _mesOptions;
    private readonly IHomogenizationMesItemPayloadBuilder _payloadBuilder;
    private readonly IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> _parameters;

    public HomogenizationMesChannel(
        MesRequestExecutor requestExecutor,
        IModuleParamRoleProvider moduleParamRoleProvider,
        IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> parameters,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationMesOptions> mesOptions,
        IHomogenizationMesItemPayloadBuilder payloadBuilder)
        : base(DependencyInjection.ModuleKey, logger, requestExecutor, moduleParamRoleProvider, productionTime)
    {
        _parameters = parameters;
        _mesOptions = mesOptions.Value;
        _payloadBuilder = payloadBuilder;
    }

    /// <summary>
    /// 匀浆 MES 签名令牌，来自插件配置，用于 Application 基类统一生成 sign。
    /// </summary>
    protected override string SignToken => _mesOptions.SignToken;

    public override async Task<MesCallResult<HomogenizationMainPlan>> GetMainPlanAsync(
        HomogenizationMainPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UpperComputerNo))
        {
            return MesCallResult<HomogenizationMainPlan>.InvalidContext("上位机编码不能为空。");
        }

        var relativePath = await ResolveMesPathAsync(
                HomogenizationParams.Mes.OrderPath,
                cancellationToken)
            .ConfigureAwait(false);

        var query = new Dictionary<string, string?>
        {
            ["upperComputerNo"] = request.UpperComputerNo.Trim(),
            ["timestamp"] = FormatTimestamp(request.Timestamp)
        };

        return await ExecuteMesGetAsync(
                relativePath,
                query,
                HomogenizationMesResponseParser.ParseMainPlan,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<MesCallResult<HomogenizationTraceBatchResult>> GenerateTraceBatchNumberAsync(
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

        var relativePath = await ResolveMesPathAsync(
                HomogenizationParams.Mes.BatchNumberPath,
                cancellationToken)
            .ConfigureAwait(false);

        var payload = new
        {
            masterPlanCode = request.MasterPlanCode.Trim(),
            operationCode = request.OperationCode.Trim()
        };

        return await ExecuteMesPostAsync(
                relativePath,
                payload,
                HomogenizationMesResponseParser.ParseTraceBatch,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 进站校验只需要托盘码；托盘码如何读取、何时触发仍由匀浆 PLC 任务负责。
    /// </summary>
    public override async Task<MesCallResult> UploadInboundAsync(
        DeviceSession? device,
        string trayCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trayCode))
        {
            return MesCallResult.InvalidContext("托盘码不能为空。");
        }

        var relativePath = await ResolveMesPathAsync(
                HomogenizationParams.Mes.InboundPath,
                cancellationToken)
            .ConfigureAwait(false);

        return await ExecuteMesAsync(
            device,
            relativePath,
            // 进站接口沿用 MES 文档中的托盘字段结构，不把这些匀浆业务字段上移到共享层。
            envelope => new
            {
                upperComputerNo = envelope.UpperComputerNo,
                timestamp = envelope.Timestamp,
                sign = envelope.Sign,
                stationNo = envelope.StationNo,
                data = new
                {
                    stackTrayNo = trayCode,
                    weldTrayNo = trayCode,
                    productNo = trayCode,
                    devices = (object?)null,
                    boms = (object?)null
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 出料上传使用完整匀浆电芯数据构造 produce 列表，失败补偿时保存的也是这条电芯记录 JSON。
    /// </summary>
    public override async Task<MesCallResult> UploadOutboundAsync(
        DeviceSession? device,
        HomogenizationCellData cellData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cellData);

        if (string.IsNullOrWhiteSpace(cellData.TrayCode))
        {
            return MesCallResult.InvalidContext("出料托盘码不能为空。");
        }

        var relativePath = await ResolveMesPathAsync(
                HomogenizationParams.Mes.OutboundPath,
                cancellationToken)
            .ConfigureAwait(false);

        return await ExecuteMesAsync(
            device,
            relativePath,
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
    public override async Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        HomogenizationRealtimeSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var relativePath = await ResolveMesPathAsync(
                HomogenizationParams.Mes.RealtimePath,
                cancellationToken)
            .ConfigureAwait(false);

        return await ExecuteMesAsync(
            device,
            relativePath,
            envelope => new
            {
                upperComputerNo = envelope.UpperComputerNo,
                timestamp = envelope.Timestamp,
                sign = envelope.Sign,
                stationNo = envelope.StationNo,
                data = new
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
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 配方上传把每个配方数组展开为带序号的 MES item，数组长度和字段含义由匀浆插件控制。
    /// </summary>
    public override async Task<MesCallResult> UploadRecipeAsync(
        DeviceSession? device,
        HomogenizationRecipeSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var relativePath = await ResolveMesPathAsync(
                HomogenizationParams.Mes.RecipePath,
                cancellationToken)
            .ConfigureAwait(false);

        return await ExecuteMesAsync(
            device,
            relativePath,
            envelope => new
            {
                upperComputerNo = envelope.UpperComputerNo,
                timestamp = envelope.Timestamp,
                sign = envelope.Sign,
                stationNo = envelope.StationNo,
                data = new
                {
                    devices = _payloadBuilder.BuildRecipeItems(snapshot)
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 设备状态上传只封装匀浆状态快照，状态码到文本的解释保留在匀浆配置。
    /// </summary>
    public override async Task<MesCallResult> UploadEquipmentStatusAsync(
        DeviceSession? device,
        HomogenizationEquipmentStatusSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var relativePath = await ResolveMesPathAsync(
                HomogenizationParams.Mes.EquipmentStatusPath,
                cancellationToken)
            .ConfigureAwait(false);

        return await ExecuteMesAsync(
            device,
            relativePath,
            envelope => new
            {
                upperComputerNo = envelope.UpperComputerNo,
                timestamp = envelope.Timestamp,
                sign = envelope.Sign,
                stationNo = envelope.StationNo,
                data = new
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
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    protected override Task<MesCallResult> UploadOutboundRecordAsync(
        DeviceSession? device,
        HomogenizationCellData cellData,
        CellCompletedRecord record,
        CancellationToken cancellationToken)
        => UploadOutboundAsync(device, cellData, cancellationToken);

    private async Task<string> ResolveMesPathAsync(
        HomogenizationParams.Mes pathKey,
        CancellationToken cancellationToken)
    {
        var snapshot = await _parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        var configuredPath = snapshot.Mes<string>(pathKey);
        return string.IsNullOrWhiteSpace(configuredPath)
            ? throw new InvalidOperationException($"Homogenization MES path is empty: {pathKey}.")
            : configuredPath.Trim();
    }
}
