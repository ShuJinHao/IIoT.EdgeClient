using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Production.Tasks;
using IIoT.Edge.Module.Sdk.Signals;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Production;

/// <summary>
/// 匀浆 PLC 运行时任务工厂，按握手任务、心跳、实时上传的顺序装配任务。
/// </summary>
public sealed class HomogenizationStationRuntimeFactory : IStationRuntimeFactory
{
    private const string HeartbeatTaskKey = "Homogenization.Heartbeat";
    private const string InboundTaskKey = "Homogenization.Inbound";
    private const string OutboundTaskKey = "Homogenization.Outbound";
    private const string RecipeTaskKey = "Homogenization.Recipe";
    private const string EquipmentStatusTaskKey = "Homogenization.EquipmentStatus";
    private const string RealtimeTaskKey = "Homogenization.Realtime";

    private static readonly HomogenizationPlcSignals.SingleRead[] RealtimeSignals =
    [
        HomogenizationPlcSignals.SingleRead.实时搅拌转速,
        HomogenizationPlcSignals.SingleRead.实时搅拌电流,
        HomogenizationPlcSignals.SingleRead.实时分散转速,
        HomogenizationPlcSignals.SingleRead.实时分散电流,
        HomogenizationPlcSignals.SingleRead.实时温度,
        HomogenizationPlcSignals.SingleRead.实时真空度
    ];

    private static readonly HomogenizationPlcSignals.ContinuousRead[] RecipeSignals =
    [
        HomogenizationPlcSignals.ContinuousRead.配方搅拌转速,
        HomogenizationPlcSignals.ContinuousRead.配方分散转速,
        HomogenizationPlcSignals.ContinuousRead.配方NCM,
        HomogenizationPlcSignals.ContinuousRead.配方SP1,
        HomogenizationPlcSignals.ContinuousRead.配方NMP,
        HomogenizationPlcSignals.ContinuousRead.配方胶液,
        HomogenizationPlcSignals.ContinuousRead.配方CNT,
        HomogenizationPlcSignals.ContinuousRead.配方真空,
        HomogenizationPlcSignals.ContinuousRead.配方时间,
        HomogenizationPlcSignals.ContinuousRead.配方温度,
        HomogenizationPlcSignals.ContinuousRead.配方停机步
    ];

    private static readonly HomogenizationPlcSignals.SingleRead[] OutboundSignals =
    [
        HomogenizationPlcSignals.SingleRead.出料CNT实际值,
        HomogenizationPlcSignals.SingleRead.出料CNT目标值,
        HomogenizationPlcSignals.SingleRead.出料CNTA罐重量,
        HomogenizationPlcSignals.SingleRead.出料CNTB罐重量,
        HomogenizationPlcSignals.SingleRead.出料NMP实际值,
        HomogenizationPlcSignals.SingleRead.出料NMP目标值,
        HomogenizationPlcSignals.SingleRead.出料胶液实际值,
        HomogenizationPlcSignals.SingleRead.出料设定搅拌时间,
        HomogenizationPlcSignals.SingleRead.出料剩余搅拌时间,
        HomogenizationPlcSignals.SingleRead.出料设定分散时间,
        HomogenizationPlcSignals.SingleRead.出料剩余分散时间
    ];

    private static readonly IReadOnlyCollection<TaskCandidate> TaskCandidates =
    [
        PlcTaskCandidateBuilder.Create(HeartbeatTaskKey, "心跳")
            .HeartbeatLike()
            .RequiresInteraction(HomogenizationPlcSignals.Interaction.心跳)
            .Build(),
        PlcTaskCandidateBuilder.Create(InboundTaskKey, "扫码进站")
            .RequiresInteraction(HomogenizationPlcSignals.Interaction.扫码进站)
            .RequiresRead(HomogenizationPlcSignals.ContinuousRead.托盘码)
            .Build(),
        PlcTaskCandidateBuilder.Create(OutboundTaskKey, "出料上传")
            .RequiresInteraction(HomogenizationPlcSignals.Interaction.出料上传)
            .RequiresRead(HomogenizationPlcSignals.ContinuousRead.托盘码)
            .RequiresRead(RealtimeSignals)
            .RequiresRead(OutboundSignals)
            .RequiresRead(HomogenizationPlcSignals.SingleRead.设备状态值)
            .Build(),
        PlcTaskCandidateBuilder.Create(RecipeTaskKey, "工艺参数上传")
            .RequiresInteraction(HomogenizationPlcSignals.Interaction.工艺参数上传)
            .RequiresRead(RecipeSignals)
            .Build(),
        PlcTaskCandidateBuilder.Create(EquipmentStatusTaskKey, "设备状态上传")
            .DefaultEnabled()
            .RequiresInteraction(HomogenizationPlcSignals.Interaction.设备状态上传)
            .RequiresRead(HomogenizationPlcSignals.SingleRead.设备状态值)
            .Build(),
        PlcTaskCandidateBuilder.Create(RealtimeTaskKey, "实时数据上传")
            .DefaultEnabled()
            .RequiresRead(RealtimeSignals)
            .Build()
    ];

    /// <summary>
    /// 工厂归属的匀浆模块标识。
    /// </summary>
    public string ModuleId => DependencyInjection.ModuleKey;

    public IReadOnlyCollection<TaskCandidate> GetTaskCandidates()
        => TaskCandidates;

    /// <summary>
    /// 基于宿主创建的匀浆上下文和当前 PLC 缓冲区创建运行任务。
    /// </summary>
    public List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context,
        IReadOnlySet<string> enabledTaskKeys)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(enabledTaskKeys);

        if (context is not HomogenizationContext homogenizationContext)
        {
            throw new InvalidOperationException("匀浆运行时需要由 ProductionContextStore 创建 HomogenizationContext。");
        }

        if (enabledTaskKeys.Count == 0)
        {
            return [];
        }

        var logger = serviceProvider.GetRequiredService<ILogService>();
        var productionTime = serviceProvider.GetRequiredService<IProductionTimeProvider>();
        var signalBindingStore = serviceProvider.GetRequiredService<IProductionContextSignalBindingStore>();
        var interactionProfile = serviceProvider.GetRequiredService<IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction>>();
        var singleReadProfile = serviceProvider.GetRequiredService<IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead>>();
        var continuousReadProfile = serviceProvider.GetRequiredService<IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead>>();
        var interactionSignals = BufferLogicalSignalAccessor<HomogenizationPlcSignals.Interaction>.Create(
            buffer,
            homogenizationContext,
            signalBindingStore,
            interactionProfile);
        var singleReadSignals = BufferLogicalSignalAccessor<HomogenizationPlcSignals.SingleRead>.Create(
            buffer,
            homogenizationContext,
            signalBindingStore,
            singleReadProfile);
        var continuousReadSignals = BufferLogicalSignalAccessor<HomogenizationPlcSignals.ContinuousRead>.Create(
            buffer,
            homogenizationContext,
            signalBindingStore,
            continuousReadProfile);
        var validator = serviceProvider.GetService<HomogenizationCellDataValidator>() ?? new HomogenizationCellDataValidator();
        var moduleOptions = serviceProvider.GetRequiredService<IOptions<HomogenizationModuleOptions>>();
        var codeOptions = serviceProvider.GetRequiredService<IOptions<HomogenizationCodeOptions>>();
        var interaction = new HomogenizationPlcHandshakeAccessor(interactionSignals, codeOptions.Value.Plc);
        var codec = new HomogenizationSignalCodec(singleReadSignals, continuousReadSignals, productionTime);
        var tasks = new List<IPlcTask>();
        IDeviceService? deviceService = null;
        IMesUploadDiagnosticsStore? diagnosticsStore = null;
        ICloudUploadDiagnosticsStore? cloudDiagnosticsStore = null;
        ICloudExecutionPolicy? cloudExecutionPolicy = null;
        IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>? moduleParameters = null;
        IDataPipelineService? dataPipelineService = null;
        IHomogenizationProductionGate? productionGate = null;

        IDeviceService GetDeviceService()
            => deviceService ??= serviceProvider.GetRequiredService<IDeviceService>();

        IMesUploadDiagnosticsStore GetDiagnosticsStore()
            => diagnosticsStore ??= serviceProvider.GetRequiredService<IMesUploadDiagnosticsStore>();

        ICloudUploadDiagnosticsStore GetCloudDiagnosticsStore()
            => cloudDiagnosticsStore ??= serviceProvider.GetRequiredService<ICloudUploadDiagnosticsStore>();

        ICloudExecutionPolicy GetCloudExecutionPolicy()
            => cloudExecutionPolicy ??= serviceProvider.GetRequiredService<ICloudExecutionPolicy>();

        IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> GetModuleParameters()
            => moduleParameters ??= serviceProvider.GetRequiredService<IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>>();

        IDataPipelineService GetDataPipelineService()
            => dataPipelineService ??= serviceProvider.GetRequiredService<IDataPipelineService>();

        IHomogenizationProductionGate GetProductionGate()
            => productionGate ??= serviceProvider.GetRequiredService<IHomogenizationProductionGate>();

        if (enabledTaskKeys.Contains(InboundTaskKey))
        {
            tasks.Add(new HomogenizationInboundTask(
                buffer,
                interaction,
                codec,
                homogenizationContext,
                GetDeviceService(),
                GetDataPipelineService(),
                GetDiagnosticsStore(),
                GetCloudDiagnosticsStore(),
                GetCloudExecutionPolicy(),
                GetModuleParameters(),
                GetProductionGate(),
                logger,
                productionTime,
                moduleOptions,
                codeOptions));
        }

        if (enabledTaskKeys.Contains(OutboundTaskKey))
        {
            tasks.Add(new HomogenizationOutboundTask(
                buffer,
                interaction,
                codec,
                homogenizationContext,
                GetDeviceService(),
                GetDataPipelineService(),
                validator,
                GetDiagnosticsStore(),
                GetCloudDiagnosticsStore(),
                GetCloudExecutionPolicy(),
                GetModuleParameters(),
                GetProductionGate(),
                logger,
                productionTime,
                moduleOptions,
                codeOptions));
        }

        if (enabledTaskKeys.Contains(RecipeTaskKey))
        {
            tasks.Add(new HomogenizationRecipeTask(
                buffer,
                interaction,
                codec,
                homogenizationContext,
                GetDataPipelineService(),
                GetDiagnosticsStore(),
                GetCloudDiagnosticsStore(),
                GetCloudExecutionPolicy(),
                GetModuleParameters(),
                GetProductionGate(),
                logger,
                productionTime,
                moduleOptions,
                codeOptions));
        }

        if (enabledTaskKeys.Contains(EquipmentStatusTaskKey))
        {
            tasks.Add(new HomogenizationEquipmentStatusTask(
                buffer,
                interaction,
                codec,
                homogenizationContext,
                GetDataPipelineService(),
                GetDiagnosticsStore(),
                GetCloudDiagnosticsStore(),
                GetCloudExecutionPolicy(),
                GetModuleParameters(),
                logger,
                productionTime,
                moduleOptions,
                codeOptions));
        }

        if (enabledTaskKeys.Contains(HeartbeatTaskKey))
        {
            tasks.Add(new HomogenizationHeartbeatTask(
                buffer,
                interactionSignals,
                homogenizationContext,
                logger,
                productionTime,
                moduleOptions));
        }

        if (enabledTaskKeys.Contains(RealtimeTaskKey))
        {
            tasks.Add(new HomogenizationRealtimeTask(
                buffer,
                codec,
                homogenizationContext,
                GetDeviceService(),
                GetDataPipelineService(),
                GetDiagnosticsStore(),
                GetCloudDiagnosticsStore(),
                GetCloudExecutionPolicy(),
                GetModuleParameters(),
                GetProductionGate(),
                logger,
                moduleOptions,
                codeOptions));
        }

        return tasks;
    }
}
