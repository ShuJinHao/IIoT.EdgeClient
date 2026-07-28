namespace IIoT.Edge.Shell.Core;

internal sealed record ShellModuleLaunchReadiness(
    bool Success,
    IReadOnlyList<string> ConfiguredModuleIds,
    IReadOnlyList<string> ActiveModuleIds,
    string? ErrorMessage)
{
    public static ShellModuleLaunchReadiness Evaluate(
        IReadOnlyList<string> configuredModuleIds,
        IReadOnlyList<string> activeModuleIds)
    {
        ArgumentNullException.ThrowIfNull(configuredModuleIds);
        ArgumentNullException.ThrowIfNull(activeModuleIds);

        var configured = Normalize(configuredModuleIds);
        var active = Normalize(activeModuleIds);
        var configuredSet = configured.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var activeSet = active.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        if (configuredSet.SetEquals(activeSet))
        {
            return new ShellModuleLaunchReadiness(
                true,
                configured,
                active,
                ErrorMessage: null);
        }

        var missing = configured
            .Where(moduleId => !activeSet.Contains(moduleId))
            .ToArray();
        var unexpected = active
            .Where(moduleId => !configuredSet.Contains(moduleId))
            .ToArray();
        var details = new List<string>();
        if (missing.Length > 0)
        {
            details.Add($"未激活：{string.Join(", ", missing)}");
        }
        if (unexpected.Length > 0)
        {
            details.Add($"非配置激活：{string.Join(", ", unexpected)}");
        }

        return new ShellModuleLaunchReadiness(
            false,
            configured,
            active,
            $"目标工序模块未就绪（{string.Join("；", details)}），请在诊断界面修复插件或配置。");
    }

    private static IReadOnlyList<string> Normalize(
        IReadOnlyList<string> moduleIds)
        => moduleIds
            .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Select(static moduleId => moduleId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static moduleId => moduleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
