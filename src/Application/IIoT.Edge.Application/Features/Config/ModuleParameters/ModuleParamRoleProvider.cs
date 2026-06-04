using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Application.Features.Config.ModuleParameters;

/// <summary>
/// 基于插件参数注册表的标准角色读取器。
/// </summary>
public sealed class ModuleParamRoleProvider(
    IModuleParamRegistry registry,
    ModuleParamValueSnapshotLoader snapshotLoader)
    : IModuleParamRoleProvider
{
    public async Task<ModuleParamRoleValue?> GetAsync(
        string moduleId,
        ModuleParamCategory category,
        ModuleParamRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        var values = await GetAllAsync(category, role, [moduleId], cancellationToken).ConfigureAwait(false);
        return values.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ModuleParamRoleValue>> GetAllAsync(
        ModuleParamCategory category,
        ModuleParamRole role,
        IReadOnlyCollection<string>? moduleIds = null,
        CancellationToken cancellationToken = default)
    {
        var moduleFilter = moduleIds is null
            ? null
            : moduleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var descriptors = registry.GetDescriptors(category)
            .Where(x => x.Role == role)
            .Where(x => moduleFilter is null || moduleFilter.Contains(x.ModuleId))
            .OrderBy(x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SortOrder)
            .ToList();

        var values = new List<ModuleParamRoleValue>();
        foreach (var descriptor in descriptors)
        {
            var snapshot = await snapshotLoader.LoadAsync(descriptor.ModuleId, cancellationToken).ConfigureAwait(false);
            var value = snapshot.Values.TryGetValue(descriptor.StorageKey, out var configured)
                ? configured
                : descriptor.DefaultValue ?? string.Empty;

            values.Add(new ModuleParamRoleValue(
                descriptor.ModuleId,
                descriptor.Category,
                descriptor.Role,
                descriptor.ValueKind,
                descriptor.Name,
                descriptor.StorageKey,
                value,
                descriptor.DefaultValue));
        }

        return values;
    }

    public async Task<string?> GetStringAsync(
        string moduleId,
        ModuleParamCategory category,
        ModuleParamRole role,
        string? defaultValue = null,
        CancellationToken cancellationToken = default)
    {
        var value = await GetAsync(moduleId, category, role, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(value?.Value) ? defaultValue : value.Value.Trim();
    }

    public async Task<string?> FirstStringAsync(
        ModuleParamCategory category,
        ModuleParamRole role,
        IReadOnlyCollection<string>? moduleIds = null,
        CancellationToken cancellationToken = default)
    {
        var values = await GetAllAsync(category, role, moduleIds, cancellationToken).ConfigureAwait(false);
        return values
            .Select(static x => x.Value?.Trim())
            .FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x));
    }

    public async Task<bool> GetBoolAsync(
        string moduleId,
        ModuleParamCategory category,
        ModuleParamRole role,
        bool defaultValue = false,
        CancellationToken cancellationToken = default)
    {
        var value = await GetAsync(moduleId, category, role, cancellationToken).ConfigureAwait(false);
        return value is null ? defaultValue : ParseBool(value.Value, defaultValue);
    }

    public async Task<bool> AnyBoolAsync(
        ModuleParamCategory category,
        ModuleParamRole role,
        IReadOnlyCollection<string>? moduleIds = null,
        bool defaultValue = false,
        CancellationToken cancellationToken = default)
    {
        var values = await GetAllAsync(category, role, moduleIds, cancellationToken).ConfigureAwait(false);
        if (values.Count == 0)
        {
            return defaultValue;
        }

        return values.Any(value => ParseBool(value.Value, defaultValue: false));
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var normalized = value.Trim();
        if (bool.TryParse(normalized, out var parsed))
        {
            return parsed;
        }

        return normalized is "1" or "是" or "启用" or "开启"
            ? true
            : normalized is "0" or "否" or "禁用" or "关闭"
                ? false
                : defaultValue;
    }
}
