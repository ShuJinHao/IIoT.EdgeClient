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
        _parameters = parameters;
    }

    /// <summary>
    /// 上传模切当前采样快照，空字段按 MES 确认传空字符串。
    /// </summary>
    public async Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        DieCuttingRealtimeSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return await ExecuteOptionalMesAsync(
            "模切采样",
            ct => GetMesPathAsync(DieCuttingParams.Mes.RealtimePath, ct),
            device,
            envelope => CreateStandardMesPayload(
                envelope,
                new
                {
                    devices = new[]
                    {
                        BuildPunchingPayload(envelope, snapshot)
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

    private object BuildPunchingPayload(MesEnvelope envelope, DieCuttingRealtimeSnapshot snapshot)
        => new
        {
            stationNo = envelope.StationNo,
            collectTime = FormatTimestamp(snapshot.CapturedAt),
            punchingLotNumber = snapshot.PunchingLotNumber,
            clipNo = snapshot.ClipNo,
            punchingDeviceCode = snapshot.PunchingDeviceCode,
            punchingDeviceName = snapshot.PunchingDeviceName,
            punchingQuantity = snapshot.PunchingQuantity,
            punchingUom = snapshot.PunchingUom,
            punchingStartTime = FormatTimestamp(snapshot.WindowStartAt),
            punchingCompleteTime = FormatTimestamp(snapshot.WindowCompleteAt),
            slittingConsumeQuantity = EmptyField,
            punchingSpeed = snapshot.PunchingSpeed,
            polePieceLength = EmptyField,
            polePieceWidth = EmptyField,
            collectorLength = EmptyField,
            collectorWidth = EmptyField,
            collectorLongMargin = EmptyField,
            collectorShortMargin = EmptyField,
            collectorFullInspectionSurface = EmptyField,
            collectorNoFullInspectionSurface = EmptyField,
            collectorWhiteMaterial = EmptyField,
            polePieceWeight = EmptyField,
            transverseBurr = EmptyField,
            longitudinalBurr = EmptyField,
            transverseBurrs = EmptyField,
            longitudinalBurrs = EmptyField,
            receivingSheetAlignment = EmptyField,
            punchingAppearance = EmptyField
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
    Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        DieCuttingRealtimeSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
