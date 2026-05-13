using IIoT.Edge.Application.Abstractions.Modules;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace IIoT.Edge.Host.Bootstrap.Core.Plugins;

public sealed class JsonEdgeProcessModuleCatalog : IEdgeProcessModuleCatalog
{
    private readonly EdgeProcessModuleCatalogOptions _options;

    public JsonEdgeProcessModuleCatalog(EdgeProcessModuleCatalogOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<IEdgeProcessModule> LoadModules()
    {
        var modules = new List<IEdgeProcessModule>();
        foreach (var manifestPath in FindManifestPaths())
        {
            var manifest = ReadManifest(manifestPath);
            if (!manifest.EntryAssembly.EndsWith(_options.EntryAssemblySuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            modules.Add(CreateModule(manifest, Path.GetDirectoryName(manifestPath)!));
        }

        return modules;
    }

    private IReadOnlyList<string> FindManifestPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in _options.SearchDirectories.Where(Directory.Exists))
        {
            AddIfExists(paths, Path.Combine(directory, "plugin.json"));
            foreach (var candidate in Directory.EnumerateFiles(directory, "plugin.json", SearchOption.AllDirectories))
            {
                AddIfExists(paths, candidate);
            }
        }

        return paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddIfExists(HashSet<string> paths, string candidate)
    {
        if (File.Exists(candidate))
        {
            paths.Add(Path.GetFullPath(candidate));
        }
    }

    private static PluginManifest ReadManifest(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOptions())
            ?? throw new InvalidOperationException($"插件清单无效：{manifestPath}");

        if (string.IsNullOrWhiteSpace(manifest.ModuleId))
        {
            throw new InvalidOperationException($"插件清单缺少 moduleId：{manifestPath}");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
        {
            throw new InvalidOperationException($"插件清单缺少 entryAssembly：{manifestPath}");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryType))
        {
            throw new InvalidOperationException($"插件清单缺少 entryType：{manifestPath}");
        }

        if (string.IsNullOrWhiteSpace(manifest.SupportedProcessType))
        {
            throw new InvalidOperationException($"插件清单缺少 supportedProcessType：{manifestPath}");
        }

        return manifest;
    }

    private static IEdgeProcessModule CreateModule(PluginManifest manifest, string pluginDirectory)
    {
        var assemblyPath = Path.Combine(pluginDirectory, manifest.EntryAssembly);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"插件入口程序集不存在：{assemblyPath}", assemblyPath);
        }

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        var entryType = assembly.GetType(manifest.EntryType, throwOnError: true)
            ?? throw new InvalidOperationException($"插件入口类型不存在：{manifest.EntryType}");

        if (!typeof(IEdgeProcessModule).IsAssignableFrom(entryType))
        {
            throw new InvalidOperationException($"插件入口类型未实现 IEdgeProcessModule：{manifest.EntryType}");
        }

        if (Activator.CreateInstance(entryType) is not IEdgeProcessModule module)
        {
            throw new InvalidOperationException($"插件入口类型无法实例化：{manifest.EntryType}");
        }

        if (!string.Equals(module.ModuleId, manifest.ModuleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"插件 ModuleId 不一致：清单={manifest.ModuleId}，入口={module.ModuleId}");
        }

        if (!string.Equals(module.ProcessType, manifest.SupportedProcessType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"插件 ProcessType 不一致：清单={manifest.SupportedProcessType}，入口={module.ProcessType}");
        }

        return module;
    }

    private static JsonSerializerOptions JsonOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

    private sealed class PluginManifest
    {
        public string ModuleId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string EntryAssembly { get; set; } = string.Empty;

        public string EntryType { get; set; } = string.Empty;

        public string SupportedProcessType { get; set; } = string.Empty;
    }
}
