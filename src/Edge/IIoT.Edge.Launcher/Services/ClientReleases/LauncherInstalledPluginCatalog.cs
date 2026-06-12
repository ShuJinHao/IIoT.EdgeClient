using System.Text.Json;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherInstalledPluginCatalog
{
    IReadOnlyList<LauncherInstalledPlugin> LoadInstalledPlugins(LauncherProfileDefinition profile);
}

public sealed class LauncherInstalledPluginCatalog : ILauncherInstalledPluginCatalog
{
    public IReadOnlyList<LauncherInstalledPlugin> LoadInstalledPlugins(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var hostDirectory = LauncherCloudApiConfigurationResolver.ResolveHostDirectory(profile);
        var roots = new[]
        {
            EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(hostDirectory)
        };

        var selected = new List<LauncherInstalledPlugin>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var plugin in LoadPluginsFromRoot(root))
            {
                selected.RemoveAll(existing =>
                    string.Equals(existing.ModuleId, plugin.ModuleId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing.ProcessType, plugin.ProcessType, StringComparison.OrdinalIgnoreCase));
                selected.Add(plugin);
            }
        }

        return selected
            .OrderBy(static plugin => plugin.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<LauncherInstalledPlugin> LoadPluginsFromRoot(string root)
    {
        foreach (var pluginDirectory in Directory.EnumerateDirectories(root))
        {
            var manifestPath = ResolveManifestPath(pluginDirectory);
            if (manifestPath is null)
            {
                continue;
            }

            var plugin = TryLoadPlugin(manifestPath);
            if (plugin is not null)
            {
                yield return plugin;
            }
        }
    }

    private static string? ResolveManifestPath(string pluginDirectory)
    {
        var direct = Path.Combine(pluginDirectory, "plugin.json");
        return File.Exists(direct) ? direct : null;
    }

    private static LauncherInstalledPlugin? TryLoadPlugin(string manifestPath)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<LauncherPluginManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions());
            if (manifest is null
                || string.IsNullOrWhiteSpace(manifest.ModuleId)
                || string.IsNullOrWhiteSpace(manifest.Version)
                || string.IsNullOrWhiteSpace(manifest.HostApiVersion))
            {
                return null;
            }

            return new LauncherInstalledPlugin(
                manifest.ModuleId.Trim(),
                manifest.SupportedProcessType?.Trim() ?? manifest.ModuleId.Trim(),
                manifest.DisplayName?.Trim() ?? manifest.ModuleId.Trim(),
                manifest.Version.Trim(),
                manifest.HostApiVersion.Trim(),
                manifest.MinHostVersion?.Trim() ?? string.Empty,
                manifest.MaxHostVersion?.Trim() ?? string.Empty,
                (manifest.Dependencies ?? [])
                    .Where(static dependency => !string.IsNullOrWhiteSpace(dependency))
                    .Select(static dependency => dependency.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                manifestPath,
                Path.GetDirectoryName(manifestPath)!);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JsonSerializerOptions JsonOptions()
        => new() { PropertyNameCaseInsensitive = true };
}
