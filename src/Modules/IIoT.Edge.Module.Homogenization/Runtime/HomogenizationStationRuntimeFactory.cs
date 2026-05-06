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
    /// <summary>
    /// 工厂归属的匀浆模块标识。
    /// </summary>
    public string ModuleId => DependencyInjection.ModuleKey;

    /// <summary>
    /// 基于宿主创建的匀浆上下文和当前 PLC 缓冲区创建运行任务。
    /// </summary>
    public List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(context);

        if (context is not HomogenizationContext homogenizationContext)
        {
            throw new InvalidOperationException("匀浆运行时需要由 ProductionContextStore 创建 HomogenizationContext。");
        }

        var logger = serviceProvider.GetRequiredService<ILogService>();
        var deviceService = serviceProvider.GetRequiredService<IDeviceService>();
        var mesChannel = serviceProvider.GetRequiredService<HomogenizationMesScenarioChannel>();
        var diagnosticsStore = serviceProvider.GetRequiredService<IMesUploadDiagnosticsStore>();
        var dataPipelineService = serviceProvider.GetRequiredService<IDataPipelineService>();
        var parameters = serviceProvider.GetRequiredService<IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>>();
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

        return
        [
            new HomogenizationInboundTask(
                buffer,
                interaction,
                codec,
                homogenizationContext,
                deviceService,
                mesChannel,
                diagnosticsStore,
                parameters,
                logger,
                productionTime,
                moduleOptions,
                codeOptions),
            new HomogenizationOutboundTask(
                buffer,
                interaction,
                codec,
                homogenizationContext,
                deviceService,
                dataPipelineService,
                validator,
                diagnosticsStore,
                parameters,
                logger,
                productionTime,
                moduleOptions,
                codeOptions),
            new HomogenizationRecipeTask(
                buffer,
                interaction,
                codec,
                homogenizationContext,
                deviceService,
                mesChannel,
                diagnosticsStore,
                logger,
                productionTime,
                moduleOptions,
                codeOptions),
            new HomogenizationEquipmentStatusTask(
                buffer,
                interaction,
                codec,
                homogenizationContext,
                deviceService,
                mesChannel,
                diagnosticsStore,
                logger,
                productionTime,
                moduleOptions,
                codeOptions),
            new HomogenizationHeartbeatTask(
                buffer,
                interaction,
                homogenizationContext,
                logger,
                productionTime,
                moduleOptions),
            new HomogenizationRealtimeTask(
                buffer,
                codec,
                homogenizationContext,
                deviceService,
                mesChannel,
                diagnosticsStore,
                logger,
                moduleOptions,
                codeOptions)
        ];
    }
}
