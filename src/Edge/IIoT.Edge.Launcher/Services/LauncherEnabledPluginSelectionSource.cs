using System.Security;
using System.Text.Json;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

public sealed record LauncherEnabledPluginSelection(
    bool ManifestIsValid,
    IReadOnlyList<string> ModuleIds)
{
    public bool Contains(string moduleId)
        => ModuleIds.Contains(moduleId, StringComparer.OrdinalIgnoreCase);
}

public interface ILauncherEnabledPluginSelectionSource
{
    LauncherEnabledPluginSelection Load();
}

public sealed class LauncherEnabledPluginSelectionSource(
    string baseDirectory,
    ILauncherStartupDiagnosticWriter? diagnostics = null)
    : ILauncherEnabledPluginSelectionSource
{
    public const string EnabledPluginsFileName = "iiot-enabled-plugins.json";

    public LauncherEnabledPluginSelection Load()
    {
        var path = Path.Combine(
            EdgeClientProgramDataPaths.ResolveLauncherDirectory(baseDirectory),
            EnabledPluginsFileName);
        if (!File.Exists(path))
        {
            ReplaceDiagnostic("LAUNCHER_PLUGIN_SELECTION_MISSING");
            return new LauncherEnabledPluginSelection(false, []);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!TryGetProperty(root, "plugins", out var plugins)
                || plugins.ValueKind != JsonValueKind.Array)
            {
                ReplaceDiagnostic("LAUNCHER_PLUGIN_SELECTION_INVALID");
                return new LauncherEnabledPluginSelection(false, []);
            }

            var entries = plugins.EnumerateArray().ToArray();
            var candidateModuleIds = entries
                .Select(static plugin => plugin.ValueKind == JsonValueKind.Object
                    ? ReadString(plugin, "moduleId")
                    : null)
                .ToArray();
            if (candidateModuleIds.Any(static moduleId =>
                    string.IsNullOrWhiteSpace(moduleId)
                    || moduleId.Length > 256
                    || moduleId.Any(char.IsControl)))
            {
                ReplaceDiagnostic("LAUNCHER_PLUGIN_SELECTION_INVALID");
                return new LauncherEnabledPluginSelection(false, []);
            }

            var moduleIds = candidateModuleIds
                .Select(static moduleId => moduleId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static moduleId => moduleId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (moduleIds.Length != entries.Length)
            {
                ReplaceDiagnostic("LAUNCHER_PLUGIN_SELECTION_INVALID");
                return new LauncherEnabledPluginSelection(false, []);
            }
            if (moduleIds.Length == 0)
            {
                ReplaceDiagnostic("LAUNCHER_PLUGIN_SELECTION_EMPTY");
                return new LauncherEnabledPluginSelection(true, []);
            }

            diagnostics?.ReplaceArea(LauncherStartupDiagnosticAreas.EnabledPluginSelection, []);
            return new LauncherEnabledPluginSelection(true, moduleIds);
        }
        catch (Exception ex) when (ex is JsonException
                                       or IOException
                                       or UnauthorizedAccessException
                                       or SecurityException
                                       or ArgumentException
                                       or NotSupportedException)
        {
            ReplaceDiagnostic(
                "LAUNCHER_PLUGIN_SELECTION_UNREADABLE",
                ex.GetType().Name);
            return new LauncherEnabledPluginSelection(false, []);
        }
    }

    private void ReplaceDiagnostic(string reasonCode, string? exceptionType = null)
        => diagnostics?.ReplaceArea(
            LauncherStartupDiagnosticAreas.EnabledPluginSelection,
            [
                new LauncherStartupDiagnostic(
                    LauncherStartupDiagnosticAreas.EnabledPluginSelection,
                    reasonCode,
                    LauncherStartupDiagnosticRepairTargets.PluginSelection,
                    ExceptionType: exceptionType)
            ]);

    private static string? ReadString(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
