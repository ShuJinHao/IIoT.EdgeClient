namespace IIoT.Edge.Module.Contracts.Config;

public static class ModuleParamRoleProviderExtensions
{
    public static Task<string?> GetMesStringAsync(
        this IModuleParamRoleProvider provider,
        string moduleId,
        ModuleParamRole role,
        string? defaultValue = null,
        CancellationToken cancellationToken = default)
        => provider.GetStringAsync(
            moduleId,
            ModuleParamCategory.Mes,
            role,
            defaultValue,
            cancellationToken);

    public static Task<string?> FirstMesStringAsync(
        this IModuleParamRoleProvider provider,
        ModuleParamRole role,
        IReadOnlyCollection<string>? moduleIds = null,
        CancellationToken cancellationToken = default)
        => provider.FirstStringAsync(
            ModuleParamCategory.Mes,
            role,
            moduleIds,
            cancellationToken);

    public static Task<bool> GetMesBoolAsync(
        this IModuleParamRoleProvider provider,
        string moduleId,
        ModuleParamRole role,
        bool defaultValue = false,
        CancellationToken cancellationToken = default)
        => provider.GetBoolAsync(
            moduleId,
            ModuleParamCategory.Mes,
            role,
            defaultValue,
            cancellationToken);

    public static Task<bool> AnyMesBoolAsync(
        this IModuleParamRoleProvider provider,
        ModuleParamRole role,
        IReadOnlyCollection<string>? moduleIds = null,
        bool defaultValue = false,
        CancellationToken cancellationToken = default)
        => provider.AnyBoolAsync(
            ModuleParamCategory.Mes,
            role,
            moduleIds,
            defaultValue,
            cancellationToken);
}
