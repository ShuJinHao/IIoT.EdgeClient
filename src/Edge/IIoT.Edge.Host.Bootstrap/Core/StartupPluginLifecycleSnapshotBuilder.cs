using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Host.Bootstrap.Modules;

namespace IIoT.Edge.Shell.Core;

public interface IStartupPluginLifecycleSnapshotBuilder
{
    IReadOnlyList<PluginLifecycleSnapshot> Build(
        IEnumerable<ModulePluginDescriptor> discoveredModules,
        IReadOnlyCollection<ModuleCatalogIssue> moduleCatalogIssues,
        IReadOnlyCollection<string> configuredEnabledModuleIds,
        IEnumerable<string> activatedModuleIds);
}

public sealed class StartupPluginLifecycleSnapshotBuilder : IStartupPluginLifecycleSnapshotBuilder
{
    public IReadOnlyList<PluginLifecycleSnapshot> Build(
        IEnumerable<ModulePluginDescriptor> discoveredModules,
        IReadOnlyCollection<ModuleCatalogIssue> moduleCatalogIssues,
        IReadOnlyCollection<string> configuredEnabledModuleIds,
        IEnumerable<string> activatedModuleIds)
    {
        var configuredEnabledSet = configuredEnabledModuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activatedModuleSet = activatedModuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issueLookup = moduleCatalogIssues
            .Where(static issue => !string.IsNullOrWhiteSpace(issue.ModuleId))
            .GroupBy(issue => issue.ModuleId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var snapshots = discoveredModules
            .OrderBy(descriptor => descriptor.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(descriptor => BuildSnapshot(descriptor, configuredEnabledSet, activatedModuleSet, issueLookup))
            .ToList();

        foreach (var issue in moduleCatalogIssues.Where(static issue => string.Equals(issue.Code, "PLUGIN_MANIFEST_INVALID", StringComparison.OrdinalIgnoreCase)))
        {
            var moduleId = issue.ModuleId ?? issue.PluginDirectoryName ?? "未知插件";
            if (snapshots.Any(x => string.Equals(x.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            snapshots.Add(new PluginLifecycleSnapshot(
                moduleId,
                issue.PluginDirectoryName ?? moduleId,
                null,
                "--",
                PluginLifecycleState.ManifestInvalid,
                issue.Message));
        }

        return snapshots
            .OrderBy(snapshot => snapshot.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PluginLifecycleSnapshot BuildSnapshot(
        ModulePluginDescriptor descriptor,
        IReadOnlySet<string> configuredEnabledSet,
        IReadOnlySet<string> activatedModuleSet,
        IReadOnlyDictionary<string, ModuleCatalogIssue[]> issueLookup)
    {
        var message = "插件已发现。";
        var state = PluginLifecycleState.Discovered;

        if (issueLookup.TryGetValue(descriptor.ModuleId, out var moduleIssues))
        {
            var issueState = TryResolveIssueState(descriptor, moduleIssues);
            if (issueState is not null)
            {
                return issueState;
            }
        }

        if (!configuredEnabledSet.Contains(descriptor.ModuleId))
        {
            state = PluginLifecycleState.DisabledByConfig;
            message = "插件已发现，但当前配置未启用。";
        }
        else if (activatedModuleSet.Contains(descriptor.ModuleId))
        {
            state = PluginLifecycleState.Activated;
            message = "插件已启用并激活。";
        }

        return new PluginLifecycleSnapshot(
            descriptor.ModuleId,
            descriptor.DisplayName,
            descriptor.ProcessType,
            descriptor.Version,
            state,
            message);
    }

    private static PluginLifecycleSnapshot? TryResolveIssueState(
        ModulePluginDescriptor descriptor,
        IReadOnlyCollection<ModuleCatalogIssue> moduleIssues)
    {
        var issue = moduleIssues.FirstOrDefault(static x => string.Equals(x.Code, "PLUGIN_HOST_VERSION_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase));
        if (issue is not null)
        {
            return CreatePluginIssueSnapshot(descriptor, PluginLifecycleState.HostVersionIncompatible, issue.Message);
        }

        issue = moduleIssues.FirstOrDefault(static x => string.Equals(x.Code, "PLUGIN_DEPENDENCY_MISSING", StringComparison.OrdinalIgnoreCase));
        if (issue is not null)
        {
            return CreatePluginIssueSnapshot(descriptor, PluginLifecycleState.DependencyMissing, issue.Message);
        }

        issue = moduleIssues.FirstOrDefault(static x => string.Equals(x.Code, "PLUGIN_LOAD_FAILED", StringComparison.OrdinalIgnoreCase));
        return issue is null
            ? null
            : CreatePluginIssueSnapshot(descriptor, PluginLifecycleState.LoadFailed, issue.Message);
    }

    private static PluginLifecycleSnapshot CreatePluginIssueSnapshot(
        ModulePluginDescriptor descriptor,
        PluginLifecycleState state,
        string message)
        => new(
            descriptor.ModuleId,
            descriptor.DisplayName,
            descriptor.ProcessType,
            descriptor.Version,
            state,
            message);
}
