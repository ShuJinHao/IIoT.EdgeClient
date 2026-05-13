namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 宿主按通用角色读取插件参数的入口，不暴露具体插件枚举。
/// </summary>
public interface IModuleParamRoleProvider
{
    Task<ModuleParamRoleValue?> GetAsync(
        string moduleId,
        ModuleParamCategory category,
        ModuleParamRole role,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModuleParamRoleValue>> GetAllAsync(
        ModuleParamCategory category,
        ModuleParamRole role,
        IReadOnlyCollection<string>? moduleIds = null,
        CancellationToken cancellationToken = default);

    Task<string?> GetStringAsync(
        string moduleId,
        ModuleParamCategory category,
        ModuleParamRole role,
        string? defaultValue = null,
        CancellationToken cancellationToken = default);

    Task<string?> FirstStringAsync(
        ModuleParamCategory category,
        ModuleParamRole role,
        IReadOnlyCollection<string>? moduleIds = null,
        CancellationToken cancellationToken = default);

    Task<bool> GetBoolAsync(
        string moduleId,
        ModuleParamCategory category,
        ModuleParamRole role,
        bool defaultValue = false,
        CancellationToken cancellationToken = default);

    Task<bool> AnyBoolAsync(
        ModuleParamCategory category,
        ModuleParamRole role,
        IReadOnlyCollection<string>? moduleIds = null,
        bool defaultValue = false,
        CancellationToken cancellationToken = default);
}
