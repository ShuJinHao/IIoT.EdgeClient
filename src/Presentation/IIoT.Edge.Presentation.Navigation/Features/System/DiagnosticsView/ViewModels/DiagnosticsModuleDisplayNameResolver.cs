using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Presentation.Navigation.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

internal sealed class DiagnosticsModuleDisplayNameResolver(LocalizedSyncDiagnosticsText diagnosticsText)
{
    public IReadOnlyDictionary<string, string> BuildModuleNameMap(StartupDiagnosticsReport report)
    {
        var pairs = report.PluginStates
            .Select(x => new KeyValuePair<string, string>(
                x.ModuleId,
                ResolveProcessDisplayName(x.ProcessType, x.DisplayName)))
            .Concat(report.ModuleRegistrations.Select(x => new KeyValuePair<string, string>(
                x.ModuleId,
                diagnosticsText.FormatProcessType(x.ProcessType))));

        return pairs
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.Select(y => y.Value).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)) ?? x.Key,
                StringComparer.OrdinalIgnoreCase);
    }

    public string ResolveModuleDisplayName(
        string moduleId,
        IReadOnlyDictionary<string, string> moduleNameMap)
    {
        if (moduleNameMap.TryGetValue(moduleId, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        return diagnosticsText.FormatProcessType(moduleId);
    }

    public string ResolveProcessDisplayName(
        string moduleId,
        string? processType,
        IReadOnlyDictionary<string, string> moduleNameMap)
    {
        if (moduleNameMap.TryGetValue(moduleId, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        return diagnosticsText.FormatProcessType(processType);
    }

    public string ResolveProcessDisplayName(string? processType, string? processDisplayName)
        => string.IsNullOrWhiteSpace(processDisplayName)
            ? diagnosticsText.FormatProcessType(processType)
            : processDisplayName;
}

