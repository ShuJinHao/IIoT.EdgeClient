using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace IIoT.Edge.Host.Bootstrap.Modules;

public interface IModulePluginAssemblyResolver
{
    Assembly LoadAssembly(string assemblyPath, string pluginDirectory);
}

public sealed class ModulePluginAssemblyResolver : IModulePluginAssemblyResolver, IDisposable
{
    private static readonly HashSet<string> SharedContractAssemblyNames = new(
        [
            "IIoT.Edge.Application", "IIoT.Edge.Domain", "IIoT.Edge.Module.Sdk",
            "IIoT.Edge.Presentation.Navigation", "IIoT.Edge.SharedKernel",
            "IIoT.Edge.UI.Shared", "Avalonia", "Avalonia.Base",
            "Avalonia.Controls", "Avalonia.Controls.DataGrid",
            "Avalonia.Markup", "Avalonia.Markup.Xaml",
            "Avalonia.Remote.Protocol", "MediatR", "MediatR.Contracts",
            "Microsoft.Extensions.Configuration", "Microsoft.Extensions.Configuration.Abstractions",
            "Microsoft.Extensions.Configuration.Binder", "Microsoft.Extensions.Configuration.FileExtensions",
            "Microsoft.Extensions.Configuration.Json", "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Diagnostics.Abstractions", "Microsoft.Extensions.FileProviders.Abstractions",
            "Microsoft.Extensions.FileProviders.Physical", "Microsoft.Extensions.FileSystemGlobbing",
            "Microsoft.Extensions.Hosting.Abstractions", "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Options", "Microsoft.Extensions.Options.ConfigurationExtensions",
            "Microsoft.Extensions.Primitives"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> FrameworkAssemblyNames = BuildFrameworkAssemblyNames();
    private static readonly HashSet<string> ForbiddenHostAssemblyNames = new(
        [
            "IIoT.Edge.Host.Bootstrap",
            "IIoT.Edge.Host.DataPipeline",
            "IIoT.Edge.Infrastructure.CloudClient",
            "IIoT.Edge.Infrastructure.DeviceComm",
            "IIoT.Edge.Infrastructure.Integration",
            "IIoT.Edge.Infrastructure.Persistence.Dapper",
            "IIoT.Edge.Infrastructure.Persistence.EfCore",
            "IIoT.Edge.Infrastructure.Update",
            "IIoT.Edge.Presentation.Panels",
            "IIoT.Edge.Presentation.Shell",
            "IIoT.Edge.Presentation.VisualTestData",
            "IIoT.Edge.Shell",
            "IIoT.Edge.Launcher",
            "IIoT.Edge.Installer",
            "IIoT.Edge.RuntimeLayoutSync"
        ],
        StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly Dictionary<string, PluginAssemblyLoadContext> _loadContexts =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private int _disposed;

    public Assembly LoadAssembly(string assemblyPath, string pluginDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var physicalPluginDirectory = PluginPathBoundary.ResolveExistingPhysicalPath(pluginDirectory);
        var physicalAssemblyPath = PluginPathBoundary.ResolveExistingPhysicalPath(assemblyPath);
        if (!PluginPathBoundary.IsWithin(physicalPluginDirectory, physicalAssemblyPath))
        {
            throw new InvalidOperationException(
                $"插件入口程序集的真实路径越出 staged 目录：{assemblyPath}。");
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (!_loadContexts.TryGetValue(physicalPluginDirectory, out var loadContext))
            {
                loadContext = new PluginAssemblyLoadContext(
                    physicalPluginDirectory,
                    physicalAssemblyPath,
                    ResolveSharedAssembly);
                _loadContexts.Add(physicalPluginDirectory, loadContext);
            }

            return loadContext.LoadEntryAssembly(physicalAssemblyPath);
        }
    }

    private static Assembly? ResolveSharedAssembly(AssemblyName requestedName)
    {
        if (requestedName.Name is not { Length: > 0 } simpleName ||
            (!SharedContractAssemblyNames.Contains(simpleName) &&
             !FrameworkAssemblyNames.Contains(simpleName)))
        {
            return null;
        }

        var loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
            AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), requestedName));
        if (loaded is not null)
            return loaded;

        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyName(requestedName);
        }
        catch
        {
            return null;
        }
    }

    private static HashSet<string> BuildFrameworkAssemblyNames()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mscorlib",
            "netstandard"
        };
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            return result;

        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(name))
                result.Add(name);
        }

        return result;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        PluginAssemblyLoadContext[] contexts;
        lock (_sync)
        {
            contexts = _loadContexts.Values.ToArray();
            _loadContexts.Clear();
        }

        foreach (var context in contexts)
            context.Unload();
    }

    private sealed class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string _pluginDirectory;
        private readonly Func<AssemblyName, Assembly?> _sharedAssemblyResolver;
        private readonly IReadOnlyDictionary<string, string> _localAssemblyPaths;
        private readonly object _loadSync = new();

        public PluginAssemblyLoadContext(
            string pluginDirectory,
            string entryAssemblyPath,
            Func<AssemblyName, Assembly?> sharedAssemblyResolver)
            : base($"IIoT.Edge.PluginRuntime.{Path.GetFileName(pluginDirectory)}.{Guid.NewGuid():N}", isCollectible: true)
        {
            _pluginDirectory = pluginDirectory;
            _sharedAssemblyResolver = sharedAssemblyResolver;
            _localAssemblyPaths = BuildLocalAssemblyMap(pluginDirectory, entryAssemblyPath);
        }

        public Assembly LoadEntryAssembly(string assemblyPath)
        {
            var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            lock (_loadSync)
            {
                var loaded = Assemblies.FirstOrDefault(candidate =>
                    AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), assemblyName));
                if (loaded is not null)
                {
                    EnsureExactLocation(loaded, assemblyPath);
                    return loaded;
                }

                var assembly = LoadFromAssemblyPath(assemblyPath);
                EnsureExactLocation(assembly, assemblyPath);
                return assembly;
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var shared = _sharedAssemblyResolver(assemblyName);
            if (shared is not null)
                return shared;

            if (assemblyName.Name is not { Length: > 0 } simpleName)
                throw new InvalidOperationException("插件依赖程序集缺少有效 identity。");

            if (!_localAssemblyPaths.TryGetValue(simpleName, out var path))
                throw new InvalidOperationException($"插件依赖未在 staged artifact 中提供：{simpleName}。");

            lock (_loadSync)
            {
                var loaded = Assemblies.FirstOrDefault(candidate =>
                    AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), assemblyName));
                return loaded ?? LoadFromAssemblyPath(path);
            }
        }

        private static IReadOnlyDictionary<string, string> BuildLocalAssemblyMap(
            string pluginDirectory,
            string entryAssemblyPath)
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lexicalPath in Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var physicalPath = PluginPathBoundary.ResolveExistingPhysicalPath(lexicalPath);
                if (!PluginPathBoundary.IsWithin(pluginDirectory, physicalPath))
                {
                    throw new InvalidOperationException(
                        $"插件依赖程序集的真实路径越出 staged 目录：{lexicalPath}。");
                }

                var name = AssemblyName.GetAssemblyName(physicalPath).Name;
                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidOperationException($"插件 staged artifact 包含无程序集 identity 的 DLL：{lexicalPath}。");

                var fileName = Path.GetFileNameWithoutExtension(physicalPath);
                if (!string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"插件 staged artifact 的文件名与程序集 identity 不一致：{fileName} != {name}。");

                var isEntryAssembly = PluginPathBoundary.PathEquals(physicalPath, entryAssemblyPath);
                if (!isEntryAssembly && IsForbiddenHostAssembly(name))
                {
                    throw new InvalidOperationException($"插件 staged artifact 携带未授权宿主程序集：{name}。");
                }

                if (!paths.TryAdd(name, physicalPath))
                    throw new InvalidOperationException($"插件目录包含重复程序集名：{name}。");
            }

            ValidatePluginOwnedReferences(paths, entryAssemblyPath);
            return paths;
        }

        private static void ValidatePluginOwnedReferences(
            IReadOnlyDictionary<string, string> localAssemblyPaths,
            string entryAssemblyPath)
        {
            foreach (var (assemblyName, assemblyPath) in localAssemblyPaths)
            {
                var isEntryAssembly = PluginPathBoundary.PathEquals(assemblyPath, entryAssemblyPath);
                if (!isEntryAssembly && SharedContractAssemblyNames.Contains(assemblyName))
                    continue;

                using var stream = File.OpenRead(assemblyPath);
                using var peReader = new PEReader(stream);
                var metadata = peReader.GetMetadataReader();
                foreach (var referenceHandle in metadata.AssemblyReferences)
                {
                    var referenceName = metadata.GetString(metadata.GetAssemblyReference(referenceHandle).Name);
                    if (IsForbiddenHostAssembly(referenceName))
                    {
                        throw new InvalidOperationException(
                            $"插件程序集 {assemblyName} 引用了未授权宿主程序集：{referenceName}。");
                    }
                }
            }
        }

        private static bool IsForbiddenHostAssembly(string assemblyName)
        {
            if (SharedContractAssemblyNames.Contains(assemblyName))
                return false;

            return ForbiddenHostAssemblyNames.Contains(assemblyName);
        }

        private static void EnsureExactLocation(Assembly assembly, string requestedPath)
        {
            var actualPath = string.IsNullOrWhiteSpace(assembly.Location)
                ? string.Empty
                : PluginPathBoundary.ResolveExistingPhysicalPath(assembly.Location);
            if (!PluginPathBoundary.PathEquals(actualPath, requestedPath))
            {
                throw new InvalidOperationException(
                    $"插件程序集必须从 staged artifact 装载。请求路径：'{requestedPath}'，实际路径：'{actualPath}'。");
            }
        }
    }
}
