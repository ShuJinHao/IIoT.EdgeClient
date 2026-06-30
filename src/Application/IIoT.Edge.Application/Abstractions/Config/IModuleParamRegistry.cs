namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 插件参数枚举注册表。宿主只保存枚举类型，不持有具体工序业务判断。
/// </summary>
public interface IModuleParamRegistry
{
    void Register(
        string moduleId,
        Type mesParamType,
        Type cloudParamType,
        Type businessParamType,
        IReadOnlyCollection<ModuleParamDefaultOverride>? defaultOverrides = null);

    IReadOnlyList<ModuleParamRegistration> GetRegistrations();

    IReadOnlyList<ModuleParamDescriptor> GetDescriptors(ModuleParamCategory category);

    IReadOnlyList<ModuleParamDescriptor> GetDescriptors(string moduleId, ModuleParamCategory category);

    bool TryGetRegistration(Type mesParamType, Type cloudParamType, Type businessParamType, out ModuleParamRegistration registration);
}

/// <summary>
/// 插件参数默认值覆盖项，用于同一套参数枚举在不同模块实例下拥有不同现场默认值。
/// </summary>
public sealed record ModuleParamDefaultOverride(
    ModuleParamCategory Category,
    string Name,
    string DefaultValue,
    IReadOnlyCollection<string>? LegacyDefaultValues = null);
