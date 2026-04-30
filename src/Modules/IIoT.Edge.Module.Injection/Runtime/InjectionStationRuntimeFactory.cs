using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Module.Injection.Runtime;

/// <summary>
/// 注液运行时工厂。当前注液模块只接入数据模型和云端上传，尚未接入 PLC 采集任务。
/// </summary>
public sealed class InjectionStationRuntimeFactory : IStationRuntimeFactory
{
    /// <summary>
    /// 工厂所属模块标识，供宿主按模块装配运行时任务。
    /// </summary>
    public string ModuleId => DependencyInjection.ModuleKey;

    /// <summary>
    /// 当前阶段返回空任务列表；后续接入注液 PLC 时应在插件内新增任务，不回填宿主。
    /// </summary>
    public List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(context);

        return [];
    }
}
