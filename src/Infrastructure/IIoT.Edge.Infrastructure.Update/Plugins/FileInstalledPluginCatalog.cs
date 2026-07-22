using System.Text.Json;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Infrastructure.Update.Plugins;

public sealed class FileInstalledPluginCatalog : IEdgeInstalledPluginCatalog
{
    public IReadOnlyList<EdgeInstalledPlugin> LoadInstalledPlugins(EdgeUpdateTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var roots = new[]
        {
            EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(target.HostDirectory)
        };

        var selected = new List<EdgeInstalledPlugin>();
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

    private static IEnumerable<EdgeInstalledPlugin> LoadPluginsFromRoot(string root)
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

    private static EdgeInstalledPlugin? TryLoadPlugin(string manifestPath)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<EdgePluginManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions());
            if (manifest is null
                || string.IsNullOrWhiteSpace(manifest.ModuleId)
                || string.IsNullOrWhiteSpace(manifest.Version)
                || string.IsNullOrWhiteSpace(manifest.HostApiVersion))
            {
                return null;
            }

            return new EdgeInstalledPlugin(
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
