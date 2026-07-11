using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Cloud;
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
using IIoT.Edge.Module.Sdk.DataPipeline;
using IIoT.Edge.Module.Sdk.Diagnostics;
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
    private readonly ICloudUploadDiagnosticsStore _cloudDiagnosticsStore;
    private readonly ICloudExecutionPolicy _cloudExecutionPolicy;
    private readonly IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> _parameters;
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
        ICloudUploadDiagnosticsStore cloudDiagnosticsStore,
        ICloudExecutionPolicy cloudExecutionPolicy,
        IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> parameters,
        IHomogenizationProductionGate productionGate,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationModuleOptions> moduleOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(buffer, interaction, codec, context, logger, productionTime, codeOptions, moduleOptions)
    {
        _dataPipelineService = dataPipelineService;
        _diagnosticsStore = diagnosticsStore;
        _cloudDiagnosticsStore = cloudDiagnosticsStore;
        _cloudExecutionPolicy = cloudExecutionPolicy;
        _parameters = parameters;
        _productionGate = productionGate;
    }

    /// <summary>
    /// 配方上传任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => "Homogenization.Recipe";

    protected override async Task DoCoreAsync()
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.工艺参数上传;

        await ExecuteHandshakeAsync(
            trigger,
            "工艺参数上传已触发。",
            "配方上传复位。",
            ProcessTriggerAsync,
            static ex => $"配方上传处理异常：{ex.Message}",
            message =>
            {
                ModuleContext.LastRecipeAt = ProductionTime.BusinessNow;
                ModuleContext.LastRecipeResult = message;
                ModuleUploadDiagnosticsRecorder.RecordFailure(
                    message,
                    DataPipelineUploadTargets.Mes,
                    _diagnosticsStore,
                    _cloudDiagnosticsStore,
                    new ModuleUploadDiagnosticsRoute(
                        CodeOptions.Mes.Channels.Recipe,
                        DependencyInjection.ModuleKey,
                        "plc_recipe_blocked",
                        "plc_recipe_enqueue_failed"),
                    new ModuleUploadDiagnosticsIdentity(
                        ModuleContext.DeviceName,
                        DependencyInjection.ModuleKey,
                        TaskName,
                        "配方上传"));
            }).ConfigureAwait(false);
    }

    private async Task ProcessTriggerAsync(CancellationToken cancellationToken)
    {
        const HomogenizationPlcSignals.Interaction trigger = HomogenizationPlcSignals.Interaction.工艺参数上传;
        var parameterSnapshot = await _parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        var mesEnabled = parameterSnapshot.Mes<bool>(HomogenizationParams.Mes.启用);
        var cloudEnabled = _cloudExecutionPolicy.IsEnabled;
        var uploadTargets = ResolveUploadTargets(mesEnabled, cloudEnabled);
        var diagnosticsIdentity = new ModuleUploadDiagnosticsIdentity(
            ModuleContext.DeviceName,
            DependencyInjection.ModuleKey,
            TaskName,
            "配方上传");
        if (uploadTargets == DataPipelineUploadTargets.None)
        {
            var disabled = MesCallResult.Disabled("MES/Cloud 上传已关闭，配方上传已跳过。");
            ModuleContext.LastRecipeAt = ProductionTime.BusinessNow;
            ModuleContext.LastRecipeResult = disabled.Message;
            Interaction.ReplyResult(trigger, disabled);
            return;
        }

        if (mesEnabled)
        {
            var gateResult = await _productionGate.EnsureReadyAsync(ModuleContext, cancellationToken).ConfigureAwait(false);
            if (!gateResult.IsSuccess)
            {
                ModuleUploadDiagnosticsRecorder.RecordBlocked(
                    gateResult.Message,
                    uploadTargets,
                    _diagnosticsStore,
                    _cloudDiagnosticsStore,
                    new ModuleUploadDiagnosticsRoute(
                        CodeOptions.Mes.Channels.Recipe,
                        DependencyInjection.ModuleKey,
                        "plc_recipe_blocked",
                        "plc_recipe_enqueue_failed"),
                    diagnosticsIdentity);
                ModuleContext.LastRecipeAt = ProductionTime.BusinessNow;
                ModuleContext.LastRecipeResult = gateResult.Message;
                Interaction.ReplyResult(trigger, gateResult);
                return;
            }
        }

        var snapshot = Codec.CaptureRecipeSnapshot();
        var result = await EnqueueRecipeAsync(snapshot, uploadTargets, mesEnabled, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            ModuleUploadDiagnosticsRecorder.RecordResult(
                result,
                uploadTargets,
                _diagnosticsStore,
                _cloudDiagnosticsStore,
                new ModuleUploadDiagnosticsRoute(
                    CodeOptions.Mes.Channels.Recipe,
                    DependencyInjection.ModuleKey,
                    "plc_recipe_enqueue_failed",
                    "plc_recipe_enqueue_failed"),
                diagnosticsIdentity);
        }

        ModuleContext.LastRecipeAt = snapshot.CapturedAt;
        ModuleContext.LastRecipeResult = result.Message;
        ModuleContext.LastRecipeSnapshot = snapshot;
        Interaction.ReplyResult(trigger, result);
    }

    private async Task<MesCallResult> EnqueueRecipeAsync(
        HomogenizationRecipeSnapshot snapshot,
        DataPipelineUploadTargets uploadTargets,
        bool includeMesPlanContext,
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
            UploadTargets = uploadTargets
        };

        try
        {
            var enqueueResult = await _dataPipelineService
                .EnqueueAsync(CreatePipelineRecord(cellData, includeMesPlanContext), cancellationToken)
                .ConfigureAwait(false);

            return ModuleDataPipelineEnqueueResultMapper.ToQueuedUploadResult(
                enqueueResult,
                "配方",
                uploadTargets);
        }
        catch (Exception ex)
        {
            return MesCallResult.TransportFailure($"配方上传处理异常：{ex.Message}");
        }
    }
}
