using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Production;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Module.Homogenization.Production.Tasks;

/// <summary>
/// 配方握手任务：PLC 触发后按配方连续读数据区读取数组并上传 MES。
/// </summary>
internal sealed class HomogenizationRecipeTask : HomogenizationTaskBase
{
    private readonly IDataPipelineService _dataPipelineService;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly IHomogenizationProductionGate _productionGate;

    /// <summary>
    /// 创建匀浆配方上传握手任务。
    /// </summary>
    public HomogenizationRecipeTask(
        IPlcBuffer buffer,
        HomogenizationPlcHandshakeAccessor interaction,
        HomogenizationSignalCodec codec,
        HomogenizationContext context,
        IDataPipelineService dataPipelineService,
        IMesUploadDiagnosticsStore diagnosticsStore,
        IHomogenizationProductionGate productionGate,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, interaction, codec, context, logger, productionTime, codeOptions, moduleOptions)
    {
        _dataPipelineService = dataPipelineService;
        _diagnosticsStore = diagnosticsStore;
        _productionGate = productionGate;
    }

    /// <summary>
    /// 配方上传任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.Recipe";

    protected override async Task DoCoreAsync()
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.工艺参数上传;

        await ExecuteMesSnapshotHandshakeAsync(
            trigger,
            "工艺参数上传已触发。",
            "配方上传复位。",
            "配方上传处理异常",
            CodeOptions.Mes.Channels.Recipe,
            _productionGate,
            _diagnosticsStore,
            Codec.CaptureRecipeSnapshot,
            EnqueueRecipeAsync,
            message =>
            {
                ModuleContext.LastRecipeAt = ProductionTime.BusinessNow;
                ModuleContext.LastRecipeResult = message;
            },
            (snapshot, result) =>
            {
                ModuleContext.LastRecipeAt = snapshot.CapturedAt;
                ModuleContext.LastRecipeResult = result.Message;
                ModuleContext.LastRecipeSnapshot = snapshot;
            }).ConfigureAwait(false);
    }

    private async Task<MesCallResult> EnqueueRecipeAsync(
        HomogenizationRecipeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var cellData = new HomogenizationCellData
        {
            RecordKind = HomogenizationCellData.RecordKindRecipe,
            DeviceName = ModuleContext.DeviceName,
            DeviceCode = ModuleContext.DeviceName,
            PlcDeviceId = ModuleContext.NetworkDeviceId,
            CompletedTime = snapshot.CapturedAt,
            RuntimeStatus = "配方待上传",
            RecipeSnapshot = snapshot,
            UploadTargets = DataPipelineUploadTargets.Mes
        };

        var enqueueResult = await _dataPipelineService
            .EnqueueAsync(CreatePipelineRecord(cellData), cancellationToken)
            .ConfigureAwait(false);

        return ToMesQueueResult(
            enqueueResult,
            "配方已进入 MES 上传队列。",
            "配方已接收，数据已进入溢出持久化。",
            "配方未接收，数据管道拒绝入队");
    }
}
