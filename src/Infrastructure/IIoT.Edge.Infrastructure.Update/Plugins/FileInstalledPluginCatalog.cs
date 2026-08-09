using System.Text.Json;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Infrastructure.Update.Plugins;

public sealed class FileInstalledPluginCatalog : IEdgeInstalledPluginCatalog
{
    public IReadOnlyList<EdgeInstalledPlugin> LoadInstalledPlugins(EdgeUpdateTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var runtimeBindingPath = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(
            target.HostDirectory);
        if (File.Exists(runtimeBindingPath))
        {
            return LoadRuntimeBoundPlugins(target, runtimeBindingPath);
        }

        var selected = new List<EdgeInstalledPlugin>();
        var root = EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(target.HostDirectory);
        if (Directory.Exists(root))
        {
            foreach (var plugin in LoadPluginsFromRoot(root))
            {
                selected.RemoveAll(existing =>
                    string.Equals(existing.ModuleId, plugin.ModuleId, StringComparison.OrdinalIgnoreCase));
                selected.Add(plugin);
            }
        }

        return selected
            .OrderBy(static plugin => plugin.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<EdgeInstalledPlugin> LoadRuntimeBoundPlugins(
        EdgeUpdateTarget target,
        string runtimeBindingPath)
    {
        EdgeRuntimeBindingEnvelope runtimeBinding;
        try
        {
            runtimeBinding = EdgeInstallerBindingCodec.ParseRuntime(
                File.ReadAllText(runtimeBindingPath));
        }
        catch (Exception ex) when (ex is JsonException
                                       or IOException
                                       or UnauthorizedAccessException
                                       or InvalidDataException
                                       or ArgumentException)
        {
            throw new InvalidDataException(
                "运行时 Binding 无效，已阻断插件库存和版本上报。",
                ex);
        }

        string targetClientCode;
        try
        {
            targetClientCode = EdgeClientIdentity.NormalizeClientCode(target.MachineProfile);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(
                "当前更新会话的 MachineProfile 不是有效 ClientCode。",
                ex);
        }

        var matchingBindings = runtimeBinding.Bindings
            .Where(binding => string.Equals(
                binding.ClientCode,
                targetClientCode,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingBindings.Length != 1)
        {
            throw new InvalidDataException(
                $"运行时 Binding 必须且只能包含一个当前设备项：{targetClientCode}。");
        }

        var plugins = new List<EdgeInstalledPlugin>(1);
        foreach (var binding in matchingBindings)
        {
            var clientCode = targetClientCode;

            var pluginDirectory = EdgeClientProgramDataPaths.ResolveDevicePluginDirectory(
                clientCode,
                "app",
                target.HostDirectory);
            var manifestPath = ResolveManifestPath(pluginDirectory)
                ?? throw new InvalidDataException($"设备插件 {clientCode} 缺少 plugin.json。");
            var plugin = TryLoadPlugin(manifestPath)
                ?? throw new InvalidDataException($"设备插件 {clientCode} 的 plugin.json 无效。");
            if (!string.Equals(plugin.ModuleId, binding.ModuleId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(plugin.ProcessType, binding.ProcessType, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(plugin.Version, binding.PluginVersion, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"设备插件 {clientCode} 的 Binding 与 plugin.json 不一致。");
            }

            plugins.Add(plugin with { ClientCode = clientCode });
        }

        return plugins
            .OrderBy(static plugin => plugin.ClientCode, StringComparer.OrdinalIgnoreCase)
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
                || string.IsNullOrWhiteSpace(manifest.HostApiVersion)
                || string.IsNullOrWhiteSpace(manifest.SupportedProcessType))
            {
                return null;
            }

            return new EdgeInstalledPlugin(
                manifest.ModuleId.Trim(),
                manifest.SupportedProcessType.Trim(),
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
