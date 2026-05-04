namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 插件参数枚举注册表。宿主只保存枚举类型，不持有具体工序业务判断。
/// </summary>
public interface IModuleParamRegistry
{
    void Register(string moduleId, Type mesParamType, Type cloudParamType, Type businessParamType);

    IReadOnlyList<ModuleParamRegistration> GetRegistrations();

    IReadOnlyList<ModuleParamDescriptor> GetDescriptors(ModuleParamCategory category);

    IReadOnlyList<ModuleParamDescriptor> GetDescriptors(string moduleId, ModuleParamCategory category);

    bool TryGetRegistration(Type mesParamType, Type cloudParamType, Type businessParamType, out ModuleParamRegistration registration);
}
