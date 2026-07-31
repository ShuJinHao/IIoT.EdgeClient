using System.Security;
using System.Text.Json;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

public sealed record LauncherEnabledPluginSelectionItem(
    string ModuleId,
    string PluginDirectory);

public sealed record LauncherEnabledPluginSelection(
    bool ManifestIsValid,
    IReadOnlyList<LauncherEnabledPluginSelectionItem> Plugins)
{
    internal static StringComparison PluginDirectoryComparison { get; } =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    internal static StringComparer PluginDirectoryComparer { get; } =
        PluginDirectoryComparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public IReadOnlyList<string> ModuleIds
        => Plugins.Select(static plugin => plugin.ModuleId).ToArray();

    public bool Contains(string moduleId)
        => Plugins.Any(plugin => string.Equals(
            plugin.ModuleId,
            moduleId,
            StringComparison.OrdinalIgnoreCase));

    public bool TryGetByPluginDirectory(
        string pluginDirectory,
        out LauncherEnabledPluginSelectionItem plugin)
    {
        var match = Plugins.FirstOrDefault(item => PluginDirectoryComparer.Equals(
            item.PluginDirectory,
            pluginDirectory));
        if (match is null)
        {
            plugin = default!;
            return false;
        }

        plugin = match;
        return true;
    }
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
    private const int SupportedSchemaVersion = 1;
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
            if (!TryGetProperty(root, "schemaVersion", out var schemaVersion)
                || schemaVersion.ValueKind != JsonValueKind.Number
                || !schemaVersion.TryGetInt32(out var parsedSchemaVersion)
                || parsedSchemaVersion != SupportedSchemaVersion
                || !TryGetProperty(root, "plugins", out var plugins)
                || plugins.ValueKind != JsonValueKind.Array)
            {
                ReplaceDiagnostic("LAUNCHER_PLUGIN_SELECTION_INVALID");
                return new LauncherEnabledPluginSelection(false, []);
            }

            var entries = plugins.EnumerateArray().ToArray();
            var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pluginDirectories = new HashSet<string>(
                LauncherEnabledPluginSelection.PluginDirectoryComparer);
            var selectedPlugins = new List<LauncherEnabledPluginSelectionItem>();
            foreach (var entry in entries)
            {
                var moduleId = entry.ValueKind == JsonValueKind.Object
                    ? ReadString(entry, "moduleId")
                    : null;
                var pluginDirectory = entry.ValueKind == JsonValueKind.Object
                    ? ReadString(entry, "pluginDirectory")
                    : null;
                if (!IsValidToken(moduleId)
                    || !IsSafePluginDirectory(pluginDirectory)
                    || !moduleIds.Add(moduleId!)
                    || !pluginDirectories.Add(pluginDirectory!))
                {
                    ReplaceDiagnostic("LAUNCHER_PLUGIN_SELECTION_INVALID");
                    return new LauncherEnabledPluginSelection(false, []);
                }

                selectedPlugins.Add(new LauncherEnabledPluginSelectionItem(
                    moduleId!,
                    pluginDirectory!));
            }

            if (selectedPlugins.Count == 0)
            {
                ReplaceDiagnostic("LAUNCHER_PLUGIN_SELECTION_EMPTY");
                return new LauncherEnabledPluginSelection(true, []);
            }

            diagnostics?.ReplaceArea(LauncherStartupDiagnosticAreas.EnabledPluginSelection, []);
            return new LauncherEnabledPluginSelection(
                true,
                selectedPlugins
                    .OrderBy(static plugin => plugin.ModuleId, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
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

    private static bool IsValidToken(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= 256
           && !value.Any(char.IsControl);

    private static bool IsSafePluginDirectory(string? value)
    {
        if (!IsValidToken(value))
        {
            return false;
        }

        return value is not "." and not ".."
               && !value!.Contains('/')
               && !value.Contains('\\')
               && value.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) < 0
               && !value.EndsWith(' ')
               && !value.EndsWith('.');
    }

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
