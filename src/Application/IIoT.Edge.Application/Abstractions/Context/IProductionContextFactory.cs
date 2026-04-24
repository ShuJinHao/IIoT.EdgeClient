using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Application.Abstractions.Context;

/// <summary>
/// 按模块创建专属 ProductionContext 的工厂。
/// 宿主只根据 ModuleId 选择工厂，不了解模块内部运行态字段。
/// </summary>
public interface IProductionContextFactory
{
    string ModuleId { get; }

    Type ContextType { get; }

    ProductionContext Create(string deviceName);
}
