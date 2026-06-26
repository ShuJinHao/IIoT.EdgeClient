using IIoT.Edge.Application.Abstractions.Config;
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
/// 模切 PLC 运行任务工厂，只创建只读采样上传任务。
/// </summary>
public sealed class DieCuttingStationRuntimeFactory : IStationRuntimeFactory
{
    private readonly DieCuttingModuleDefinition _definition;

    public DieCuttingStationRuntimeFactory(DieCuttingModuleDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        TaskCandidates =
        [
            PlcTaskCandidateBuilder.Create(_definition.RealtimeSampleUploadTaskKey, $"{_definition.DisplayName}采样上传")
            .RequiresRead(DieCuttingPlcSignals.SingleRead.实际产量)
            .RequiresRead(DieCuttingPlcSignals.SingleRead.冲切速度)
            .RequiresRead(DieCuttingPlcSignals.ContinuousRead.弹夹号MG1)
            .RequiresRead(DieCuttingPlcSignals.ContinuousRead.弹夹号MG2)
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

        if (!enabledTaskKeys.Contains(_definition.RealtimeSampleUploadTaskKey))
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

        return
        [
            new DieCuttingRealtimeSampleUploadTask(
                _definition,
                buffer,
                codec,
                dieCuttingContext,
                serviceProvider.GetRequiredService<IDieCuttingMesScenarioChannel>(),
                serviceProvider.GetRequiredService<DieCuttingProductionPlanService>(),
                serviceProvider.GetRequiredService<IDieCuttingProductionRecordStore>(),
                serviceProvider.GetRequiredService<IMesUploadDiagnosticsStore>(),
                serviceProvider.GetRequiredService<IPlcConnectionManager>(),
                serviceProvider.GetRequiredService<IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business>>(),
                logger,
                serviceProvider.GetRequiredService<IOptions<DieCuttingModuleOptions>>())
        ];
    }
}
