using System.Text.Json;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherProfileVisibilityService
{
    IReadOnlyList<LauncherProfileDefinition> SelectVisibleProfiles(
        IReadOnlyList<LauncherProfileDefinition> profiles);

    LauncherProfileSelection ResolveSelection(
        IReadOnlyList<LauncherProfileDefinition> profiles);
}

public sealed record LauncherProfileSelection(
    IReadOnlyList<LauncherProfileDefinition> VisibleProfiles,
    IReadOnlyList<string> EnabledModuleIds,
    IReadOnlyDictionary<string, string> ModuleProfileIds);

public sealed class LauncherProfileVisibilityService(
    string baseDirectory,
    IEdgeProfileModuleConfigurationStore moduleConfiguration,
    ILauncherUpdateTargetFactory targetFactory) : ILauncherProfileVisibilityService
{
    private const string EnabledPluginsFileName = "iiot-enabled-plugins.json";

    public IReadOnlyList<LauncherProfileDefinition> SelectVisibleProfiles(
        IReadOnlyList<LauncherProfileDefinition> profiles)
        => ResolveSelection(profiles).VisibleProfiles;

    public LauncherProfileSelection ResolveSelection(
        IReadOnlyList<LauncherProfileDefinition> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0)
        {
            return new LauncherProfileSelection(profiles, [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var selectedModuleIds = ReadEnabledPluginModuleIds();
        if (selectedModuleIds.Count == 0)
        {
            return BuildSelection(SelectMaintenanceProfiles(profiles));
        }

        var visibleProfiles = profiles
            .Where(profile => ProfileUsesAnySelectedModule(profile, selectedModuleIds))
            .ToArray();

        return visibleProfiles.Length > 0
            ? BuildSelection(visibleProfiles, selectedModuleIds)
            : BuildSelection(SelectMaintenanceProfiles(profiles));
    }

    private HashSet<string> ReadEnabledPluginModuleIds()
    {
        var path = Path.Combine(
            EdgeClientProgramDataPaths.ResolveLauncherDirectory(baseDirectory),
            EnabledPluginsFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!TryGetProperty(root, "plugins", out var plugins)
                || plugins.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plugin in plugins.EnumerateArray())
            {
                if (plugin.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var moduleId = ReadString(plugin, "moduleId");
                if (!string.IsNullOrWhiteSpace(moduleId))
                {
                    moduleIds.Add(moduleId);
                }
            }

            return moduleIds;
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private bool ProfileUsesAnySelectedModule(
        LauncherProfileDefinition profile,
        IReadOnlySet<string> selectedModuleIds)
    {
        try
        {
            var target = targetFactory.Create(profile);
            return moduleConfiguration
                .ReadEnabledModules(target)
                .Any(selectedModuleIds.Contains);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private LauncherProfileSelection BuildSelection(
        IReadOnlyList<LauncherProfileDefinition> profiles,
        IReadOnlySet<string>? selectedModuleIds = null)
    {
        var enabledModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moduleProfileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolvedProfiles = new List<LauncherProfileDefinition>(profiles.Count);
        foreach (var profile in profiles)
        {
            var profileModuleIds = ReadProfileModuleIds(profile)
                .Concat(profile.ExpectedModuleIds)
                .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
                .Select(static moduleId => moduleId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static moduleId => moduleId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            resolvedProfiles.Add(profile with
            {
                ExpectedModuleIds = profileModuleIds
            });

            foreach (var moduleId in profileModuleIds)
            {
                if (selectedModuleIds is not null && !selectedModuleIds.Contains(moduleId))
                {
                    continue;
                }

                enabledModuleIds.Add(moduleId);
                moduleProfileIds.TryAdd(moduleId, profile.ProfileId);
            }
        }

        return new LauncherProfileSelection(
            resolvedProfiles,
            enabledModuleIds.OrderBy(static moduleId => moduleId, StringComparer.OrdinalIgnoreCase).ToArray(),
            moduleProfileIds);
    }

    private IReadOnlyList<string> ReadProfileModuleIds(LauncherProfileDefinition profile)
    {
        try
        {
            return moduleConfiguration.ReadEnabledModules(targetFactory.Create(profile));
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private IReadOnlyList<LauncherProfileDefinition> SelectMaintenanceProfiles(
        IReadOnlyList<LauncherProfileDefinition> profiles)
        => profiles
            .Where(profile => ReadProfileModuleIds(profile).Count == 0)
            .ToArray();

    private static string? ReadString(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
