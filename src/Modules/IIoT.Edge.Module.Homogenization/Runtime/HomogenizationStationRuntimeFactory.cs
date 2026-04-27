using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Runtime.Tasks;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 匀浆 PLC 运行时任务工厂，按握手任务、心跳、实时上传的顺序装配任务。
/// </summary>
public sealed class HomogenizationStationRuntimeFactory : IStationRuntimeFactory
{
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
        var mesApiService = serviceProvider.GetRequiredService<IHomogenizationMesApiService>();
        var diagnosticsStore = serviceProvider.GetRequiredService<IMesUploadDiagnosticsStore>();
        var dataPipelineService = serviceProvider.GetRequiredService<IDataPipelineService>();
        var validator = serviceProvider.GetService<HomogenizationCellDataValidator>() ?? new HomogenizationCellDataValidator();
        var moduleOptions = serviceProvider.GetRequiredService<IOptions<HomogenizationModuleOptions>>();
        var codeOptions = serviceProvider.GetRequiredService<IOptions<HomogenizationCodeOptions>>();

        return
        [
            new HomogenizationInboundTask(
                buffer,
                homogenizationContext,
                deviceService,
                mesApiService,
                diagnosticsStore,
                logger,
                moduleOptions,
                codeOptions),
            new HomogenizationOutboundTask(
                buffer,
                homogenizationContext,
                deviceService,
                dataPipelineService,
                validator,
                logger,
                moduleOptions,
                codeOptions),
            new HomogenizationRecipeTask(
                buffer,
                homogenizationContext,
                deviceService,
                mesApiService,
                diagnosticsStore,
                logger,
                moduleOptions,
                codeOptions),
            new HomogenizationEquipmentStatusTask(
                buffer,
                homogenizationContext,
                deviceService,
                mesApiService,
                diagnosticsStore,
                logger,
                moduleOptions,
                codeOptions),
            new HomogenizationHeartbeatTask(
                buffer,
                homogenizationContext,
                logger,
                moduleOptions,
                codeOptions),
            new HomogenizationRealtimeTask(
                buffer,
                homogenizationContext,
                deviceService,
                mesApiService,
                diagnosticsStore,
                logger,
                moduleOptions,
                codeOptions)
        ];
    }
}
