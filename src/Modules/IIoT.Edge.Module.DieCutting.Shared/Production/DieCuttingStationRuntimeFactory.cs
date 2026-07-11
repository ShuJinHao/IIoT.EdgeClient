using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.DieCutting.Config;
using IIoT.Edge.Module.DieCutting.Config.Io;
using IIoT.Edge.Module.DieCutting.Config.Parameters;
using IIoT.Edge.Module.DieCutting.Mes;
using IIoT.Edge.Module.DieCutting.Production.Tasks;
using IIoT.Edge.Module.Sdk.Signals;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.DieCutting.Production;

/// <summary>
/// 模切 PLC 运行任务工厂，按任务绑定创建实时数据和设备状态上传任务。
/// </summary>
public sealed class DieCuttingStationRuntimeFactory : IStationRuntimeFactory
{
    private readonly DieCuttingModuleDefinition _definition;
    private static readonly DieCuttingPlcSignals.SingleRead[] RealtimeSingleReadSignals = Enum
        .GetValues<DieCuttingPlcSignals.SingleRead>()
        .Where(static signal => signal != DieCuttingPlcSignals.SingleRead.设备状态)
        .ToArray();

    public DieCuttingStationRuntimeFactory(DieCuttingModuleDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        TaskCandidates =
        [
            PlcTaskCandidateBuilder.Create(_definition.RealtimeSampleUploadTaskKey, $"{_definition.DisplayName}实时数据上传")
            .DefaultEnabled()
            .RequiresRead(RealtimeSingleReadSignals)
            .RequiresRead(Enum.GetValues<DieCuttingPlcSignals.ContinuousRead>())
            .Build(),
            PlcTaskCandidateBuilder.Create(_definition.DeviceStatusUploadTaskKey, $"{_definition.DisplayName}设备状态上传")
            .DefaultEnabled()
            .RequiresRead(DieCuttingPlcSignals.SingleRead.设备状态)
            .Build()
        ];
    }

    private IReadOnlyCollection<TaskCandidate> TaskCandidates { get; }

    /// <summary>
    /// 工厂归属的模切模块标识。
    /// </summary>
    public string ModuleId => _definition.ModuleId;

    public IReadOnlyCollection<TaskCandidate> GetTaskCandidates()
        => TaskCandidates;

    /// <summary>
    /// 基于宿主创建的模切上下文和 PLC buffer 创建运行任务。
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

        if (context is not DieCuttingContext dieCuttingContext)
        {
            throw new InvalidOperationException("模切运行时需要由 ProductionContextStore 创建 DieCuttingContext。");
        }

        var realtimeEnabled = enabledTaskKeys.Contains(_definition.RealtimeSampleUploadTaskKey);
        var deviceStatusEnabled = enabledTaskKeys.Contains(_definition.DeviceStatusUploadTaskKey);
        if (!realtimeEnabled && !deviceStatusEnabled)
        {
            return [];
        }

        var logger = serviceProvider.GetRequiredService<ILogService>();
        var productionTime = serviceProvider.GetRequiredService<IProductionTimeProvider>();
        var signalBindingStore = serviceProvider.GetRequiredService<IProductionContextSignalBindingStore>();
        var singleReadProfile = serviceProvider.GetRequiredService<IModulePlcSignalProfile<DieCuttingPlcSignals.SingleRead>>();
        var continuousReadProfile = serviceProvider.GetRequiredService<IModulePlcSignalProfile<DieCuttingPlcSignals.ContinuousRead>>();
        var singleReadSignals = BufferLogicalSignalAccessor<DieCuttingPlcSignals.SingleRead>.Create(
            buffer,
            dieCuttingContext,
            signalBindingStore,
            singleReadProfile);
        var continuousReadSignals = BufferLogicalSignalAccessor<DieCuttingPlcSignals.ContinuousRead>.Create(
            buffer,
            dieCuttingContext,
            signalBindingStore,
            continuousReadProfile);
        var codec = new DieCuttingSignalCodec(singleReadSignals, continuousReadSignals, productionTime);

        var tasks = new List<IPlcTask>();
        if (realtimeEnabled)
        {
            tasks.Add(new DieCuttingRealtimeSampleUploadTask(
                _definition,
                buffer,
                codec,
                dieCuttingContext,
                serviceProvider.GetRequiredService<IDataPipelineService>(),
                serviceProvider.GetRequiredService<IDieCuttingProductionGate>(),
                serviceProvider.GetRequiredService<IDieCuttingProductionRecordStore>(),
                serviceProvider.GetRequiredService<IMesUploadDiagnosticsStore>(),
                serviceProvider.GetRequiredService<ICloudUploadDiagnosticsStore>(),
                serviceProvider.GetRequiredService<ICloudExecutionPolicy>(),
                serviceProvider.GetRequiredService<IPlcConnectionManager>(),
                serviceProvider.GetRequiredService<IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>>(),
                logger,
                serviceProvider.GetRequiredService<IOptions<DieCuttingModuleOptions>>()));
        }

        if (deviceStatusEnabled)
        {
            tasks.Add(new DieCuttingDeviceStatusUploadTask(
                _definition,
                buffer,
                codec,
                dieCuttingContext,
                serviceProvider.GetRequiredService<IDataPipelineService>(),
                serviceProvider.GetRequiredService<IMesUploadDiagnosticsStore>(),
                serviceProvider.GetRequiredService<ICloudUploadDiagnosticsStore>(),
                serviceProvider.GetRequiredService<ICloudExecutionPolicy>(),
                serviceProvider.GetRequiredService<IPlcConnectionManager>(),
                serviceProvider.GetRequiredService<IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>>(),
                logger,
                serviceProvider.GetRequiredService<IOptions<DieCuttingModuleOptions>>()));
        }

        return tasks;
    }
}
