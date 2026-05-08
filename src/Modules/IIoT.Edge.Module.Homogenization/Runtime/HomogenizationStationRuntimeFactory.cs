using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Runtime.Tasks;
using IIoT.Edge.Runtime.Signals;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HomogenizationMesScenarioChannel = IIoT.Edge.Application.Modules.Mes.IMesScenarioChannel<
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationCellData,
    string,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRealtimeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRecipeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationEquipmentStatusSnapshot>;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 匀浆 PLC 运行时任务工厂，按握手任务、心跳、实时上传的顺序装配任务。
/// </summary>
public sealed class HomogenizationStationRuntimeFactory : IStationRuntimeFactory
{
    private const string Read = "Read";
    private const string Write = "Write";
    private const string HeartbeatTaskKey = "Homogenization.Heartbeat";
    private const string InboundTaskKey = "Homogenization.Inbound";
    private const string OutboundTaskKey = "Homogenization.Outbound";
    private const string RecipeTaskKey = "Homogenization.Recipe";
    private const string EquipmentStatusTaskKey = "Homogenization.EquipmentStatus";
    private const string RealtimeTaskKey = "Homogenization.Realtime";

    private static readonly TaskRequiredSignal InteractionHeartbeatRead = Required("Homogenization.Interaction.Heartbeat", Read);
    private static readonly TaskRequiredSignal InteractionHeartbeatWrite = Required("Homogenization.Interaction.Heartbeat", Write);
    private static readonly TaskRequiredSignal InteractionInboundRead = Required("Homogenization.Interaction.Inbound", Read);
    private static readonly TaskRequiredSignal InteractionInboundWrite = Required("Homogenization.Interaction.Inbound", Write);
    private static readonly TaskRequiredSignal InteractionOutboundRead = Required("Homogenization.Interaction.Outbound", Read);
    private static readonly TaskRequiredSignal InteractionOutboundWrite = Required("Homogenization.Interaction.Outbound", Write);
    private static readonly TaskRequiredSignal InteractionRecipeRead = Required("Homogenization.Interaction.Recipe", Read);
    private static readonly TaskRequiredSignal InteractionRecipeWrite = Required("Homogenization.Interaction.Recipe", Write);
    private static readonly TaskRequiredSignal InteractionEquipmentStatusRead = Required("Homogenization.Interaction.EquipmentStatus", Read);
    private static readonly TaskRequiredSignal InteractionEquipmentStatusWrite = Required("Homogenization.Interaction.EquipmentStatus", Write);
    private static readonly TaskRequiredSignal TrayCodeRead = Required("Homogenization.TrayCode", Read);
    private static readonly TaskRequiredSignal EquipmentStatusValueRead = Required("Homogenization.EquipmentStatusValue", Read);
    private static readonly IReadOnlyList<TaskRequiredSignal> RealtimeSignals =
    [
        Required("Homogenization.RealtimeStirringSpeed", Read),
        Required("Homogenization.RealtimeStirringCurrent", Read),
        Required("Homogenization.RealtimeDispersionSpeed", Read),
        Required("Homogenization.RealtimeDispersionCurrent", Read),
        Required("Homogenization.RealtimeTemperature", Read),
        Required("Homogenization.RealtimeVacuum", Read)
    ];
    private static readonly IReadOnlyList<TaskRequiredSignal> RecipeSignals =
    [
        Required("Homogenization.Recipe.StirringSpeed", Read),
        Required("Homogenization.Recipe.DispersionSpeed", Read),
        Required("Homogenization.Recipe.Ncm", Read),
        Required("Homogenization.Recipe.Sp1", Read),
        Required("Homogenization.Recipe.Nmp", Read),
        Required("Homogenization.Recipe.GlueSolution", Read),
        Required("Homogenization.Recipe.Cnt", Read),
        Required("Homogenization.Recipe.Vacuum", Read),
        Required("Homogenization.Recipe.Time", Read),
        Required("Homogenization.Recipe.Temperature", Read),
        Required("Homogenization.Recipe.StopStep", Read)
    ];
    private static readonly IReadOnlyList<TaskRequiredSignal> OutboundSignals =
    [
        Required("Homogenization.Outbound.CntActual", Read),
        Required("Homogenization.Outbound.CntTarget", Read),
        Required("Homogenization.Outbound.CntTankAWeight", Read),
        Required("Homogenization.Outbound.CntTankBWeight", Read),
        Required("Homogenization.Outbound.NmpActual", Read),
        Required("Homogenization.Outbound.NmpTarget", Read),
        Required("Homogenization.Outbound.GlueActual", Read),
        Required("Homogenization.Outbound.SetStirringTime", Read),
        Required("Homogenization.Outbound.RemainingStirringTime", Read),
        Required("Homogenization.Outbound.SetDispersionTime", Read),
        Required("Homogenization.Outbound.RemainingDispersionTime", Read)
    ];
    private static readonly IReadOnlyCollection<TaskCandidate> TaskCandidates =
    [
        new(
            HeartbeatTaskKey,
            "心跳",
            [InteractionHeartbeatRead, InteractionHeartbeatWrite],
            IsHeartbeatLike: true),
        new(
            InboundTaskKey,
            "扫码进站",
            [InteractionInboundRead, InteractionInboundWrite, TrayCodeRead]),
        new(
            OutboundTaskKey,
            "出料上传",
            [InteractionOutboundRead, InteractionOutboundWrite, TrayCodeRead, ..RealtimeSignals, ..OutboundSignals, EquipmentStatusValueRead]),
        new(
            RecipeTaskKey,
            "工艺参数上传",
            [InteractionRecipeRead, InteractionRecipeWrite, ..RecipeSignals]),
        new(
            EquipmentStatusTaskKey,
            "设备状态上传",
            [InteractionEquipmentStatusRead, InteractionEquipmentStatusWrite, EquipmentStatusValueRead]),
        new(
            RealtimeTaskKey,
            "实时数据上传",
            RealtimeSignals)
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
        var interactionProfile = serviceProvider.GetRequiredService<IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction>>();
        var singleReadProfile = serviceProvider.GetRequiredService<IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead>>();
        var continuousReadProfile = serviceProvider.GetRequiredService<IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead>>();
        var interactionSignals = BufferLogicalSignalAccessor<HomogenizationPlcSignals.Interaction>.Create(
            buffer,
            homogenizationContext,
            interactionProfile);
        var singleReadSignals = BufferLogicalSignalAccessor<HomogenizationPlcSignals.SingleRead>.Create(
            buffer,
            homogenizationContext,
            singleReadProfile);
        var continuousReadSignals = BufferLogicalSignalAccessor<HomogenizationPlcSignals.ContinuousRead>.Create(
            buffer,
            homogenizationContext,
            continuousReadProfile);
        var validator = serviceProvider.GetService<HomogenizationCellDataValidator>() ?? new HomogenizationCellDataValidator();
        var moduleOptions = serviceProvider.GetRequiredService<IOptions<HomogenizationModuleOptions>>();
        var codeOptions = serviceProvider.GetRequiredService<IOptions<HomogenizationCodeOptions>>();
        var interaction = new HomogenizationPlcHandshakeAccessor(interactionSignals, codeOptions.Value.Plc);
        var codec = new HomogenizationSignalCodec(singleReadSignals, continuousReadSignals, productionTime);
        var tasks = new List<IPlcTask>();

        if (enabledTaskKeys.Contains(InboundTaskKey))
        {
            tasks.Add(new HomogenizationInboundTask(
                buffer,
                interaction,
                codec,
                homogenizationContext,
                serviceProvider.GetRequiredService<IDeviceService>(),
                serviceProvider.GetRequiredService<HomogenizationMesScenarioChannel>(),
                serviceProvider.GetRequiredService<IMesUploadDiagnosticsStore>(),
                serviceProvider.GetRequiredService<IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>>(),
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
                serviceProvider.GetRequiredService<IDeviceService>(),
                serviceProvider.GetRequiredService<IDataPipelineService>(),
                validator,
                serviceProvider.GetRequiredService<IMesUploadDiagnosticsStore>(),
                serviceProvider.GetRequiredService<IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>>(),
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
                serviceProvider.GetRequiredService<IDeviceService>(),
                serviceProvider.GetRequiredService<HomogenizationMesScenarioChannel>(),
                serviceProvider.GetRequiredService<IMesUploadDiagnosticsStore>(),
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
                serviceProvider.GetRequiredService<IDeviceService>(),
                serviceProvider.GetRequiredService<HomogenizationMesScenarioChannel>(),
                serviceProvider.GetRequiredService<IMesUploadDiagnosticsStore>(),
                logger,
                productionTime,
                moduleOptions,
                codeOptions));
        }

        if (enabledTaskKeys.Contains(HeartbeatTaskKey))
        {
            tasks.Add(new HomogenizationHeartbeatTask(
                buffer,
                interaction,
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
                serviceProvider.GetRequiredService<IDeviceService>(),
                serviceProvider.GetRequiredService<HomogenizationMesScenarioChannel>(),
                serviceProvider.GetRequiredService<IMesUploadDiagnosticsStore>(),
                logger,
                moduleOptions,
                codeOptions));
        }

        return tasks;
    }

    private static TaskRequiredSignal Required(string signalKey, string direction)
        => new(signalKey, direction);
}
