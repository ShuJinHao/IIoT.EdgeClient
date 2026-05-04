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
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using IIoT.Edge.Runtime.Signals;
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
    /// 基于宿主创建的匀浆上下文和当前 PLC 缓冲区创建 6 个运行任务。
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
        var parameters = serviceProvider.GetRequiredService<IModuleParamProvider<MesParam, CloudParam, BusinessParam>>();
        var productionTime = serviceProvider.GetRequiredService<IProductionTimeProvider>();
        var signalProfile = serviceProvider.GetRequiredService<IModulePlcSignalProfile<HomogenizationSignal>>();
        var signals = BufferLogicalSignalAccessor<HomogenizationSignal>.Create(
            buffer,
            homogenizationContext,
            signalProfile);
        var validator = serviceProvider.GetService<HomogenizationCellDataValidator>() ?? new HomogenizationCellDataValidator();
        var moduleOptions = serviceProvider.GetRequiredService<IOptions<HomogenizationModuleOptions>>();
        var codeOptions = serviceProvider.GetRequiredService<IOptions<HomogenizationCodeOptions>>();

        return
        [
            new HomogenizationInboundTask(
                buffer,
                signals,
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
                signals,
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
                signals,
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
                signals,
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
                signals,
                homogenizationContext,
                logger,
                productionTime,
                moduleOptions,
                codeOptions),
            new HomogenizationRealtimeTask(
                buffer,
                signals,
                homogenizationContext,
                deviceService,
                mesChannel,
                diagnosticsStore,
                logger,
                productionTime,
                moduleOptions,
                codeOptions)
        ];
    }
}
