using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Stacking.Config.Hardware;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Runtime.Tasks;
using IIoT.Edge.Runtime.Signals;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Stacking.Runtime;

/// <summary>
/// 叠片运行时工厂，负责把 PLC 缓冲区、逻辑信号访问器和采集任务组装起来。
/// </summary>
public sealed class StackingStationRuntimeFactory : IStationRuntimeFactory
{
    /// <summary>
    /// 工厂所属模块标识。
    /// </summary>
    public string ModuleId => StackingModuleConstants.ModuleId;

    /// <summary>
    /// 创建叠片采集任务；任务只依赖插件信号清单和 DataPipeline，不把叠片流程写入宿主。
    /// </summary>
    public List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(context);

        var signalProfile = serviceProvider.GetRequiredService<IModulePlcSignalProfile<StackingSignal>>();
        var signalAccessor = BufferLogicalSignalAccessor<StackingSignal>.Create(
            buffer,
            context,
            signalProfile);

        return
        [
            new StackingSignalCaptureTask(
                buffer,
                signalAccessor,
                context,
                serviceProvider.GetRequiredService<IDataPipelineService>(),
                serviceProvider.GetRequiredService<ILogService>())
        ];
    }
}
