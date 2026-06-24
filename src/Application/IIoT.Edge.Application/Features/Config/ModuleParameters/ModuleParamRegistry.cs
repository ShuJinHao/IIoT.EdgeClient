using System.Reflection;
using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Application.Features.Config.ModuleParameters;

/// <summary>
/// 默认插件参数注册表，负责把插件三组枚举转换为宿主可展示的参数定义。
/// </summary>
public sealed class ModuleParamRegistry : IModuleParamRegistry
{
    private readonly Dictionary<string, ModuleParamRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ModuleParamDescriptor>> _descriptors = new(StringComparer.OrdinalIgnoreCase);

    public void Register(
        string moduleId,
        Type mesParamType,
        Type cloudParamType,
        Type businessParamType,
        IReadOnlyCollection<ModuleParamDefaultOverride>? defaultOverrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ValidateEnumType(mesParamType, nameof(mesParamType));
        ValidateEnumType(cloudParamType, nameof(cloudParamType));
        ValidateEnumType(businessParamType, nameof(businessParamType));

        if (_registrations.ContainsKey(moduleId))
        {
            throw new InvalidOperationException($"模块 '{moduleId}' 的参数枚举已注册。");
        }

        _registrations[moduleId] = new ModuleParamRegistration(
            moduleId,
            mesParamType,
            cloudParamType,
            businessParamType);
        var overridesByKey = (defaultOverrides ?? [])
            .ToDictionary(
                static x => BuildOverrideKey(x.Category, x.Name),
                static x => x.DefaultValue,
                StringComparer.OrdinalIgnoreCase);

        _descriptors[moduleId] =
        [
            .. CreateDescriptors(moduleId, ModuleParamCategory.Mes, mesParamType, overridesByKey),
            .. CreateDescriptors(moduleId, ModuleParamCategory.Cloud, cloudParamType, overridesByKey),
            .. CreateDescriptors(moduleId, ModuleParamCategory.Business, businessParamType, overridesByKey)
        ];
    }

    public IReadOnlyList<ModuleParamRegistration> GetRegistrations()
        => _registrations.Values.ToList();

    public IReadOnlyList<ModuleParamDescriptor> GetDescriptors(ModuleParamCategory category)
        => _descriptors.Values
            .SelectMany(x => x)
            .Where(x => x.Category == category)
            .OrderBy(x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SortOrder)
            .ToList();

    public IReadOnlyList<ModuleParamDescriptor> GetDescriptors(string moduleId, ModuleParamCategory category)
        => _descriptors.TryGetValue(moduleId, out var descriptors)
            ? descriptors
                .Where(x => x.Category == category)
                .OrderBy(x => x.SortOrder)
                .ToList()
            : [];

    public bool TryGetRegistration(
        Type mesParamType,
        Type cloudParamType,
        Type businessParamType,
        out ModuleParamRegistration registration)
    {
        registration = _registrations.Values.FirstOrDefault(x =>
            x.MesParamType == mesParamType
            && x.CloudParamType == cloudParamType
            && x.BusinessParamType == businessParamType)!;
        return registration is not null;
    }

    private static IEnumerable<ModuleParamDescriptor> CreateDescriptors(
        string moduleId,
        ModuleParamCategory category,
        Type enumType,
        IReadOnlyDictionary<string, string> defaultOverrides)
    {
        var values = Enum.GetNames(enumType);
        for (var index = 0; index < values.Length; index++)
        {
            var name = values[index];
            var field = enumType.GetField(name, BindingFlags.Public | BindingFlags.Static);
            var attribute = field?.GetCustomAttribute<ModuleParamAttribute>();
            if (attribute is null)
            {
                continue;
            }

            var defaultValue = defaultOverrides.TryGetValue(BuildOverrideKey(category, name), out var overriddenDefault)
                ? overriddenDefault
                : attribute.DefaultValue;

            yield return new ModuleParamDescriptor(
                moduleId,
                category,
                enumType,
                name,
                ModuleParamKeys.StorageKey(moduleId, category, name),
                attribute.ValueKind,
                defaultValue,
                attribute.Unit,
                attribute.MinValue,
                attribute.MaxValue,
                attribute.Role,
                attribute.DisplayNameResourceKey,
                attribute.DisplayNameFallback,
                attribute.DescriptionResourceKey,
                attribute.DescriptionFallback,
                index + 1);
        }
    }

    private static string BuildOverrideKey(ModuleParamCategory category, string name)
        => $"{category}:{name}";

    private static void ValidateEnumType(Type enumType, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(enumType, parameterName);
        if (!enumType.IsEnum)
        {
            throw new InvalidOperationException($"模块参数类型 '{enumType.FullName}' 必须是枚举。");
        }
    }
}
