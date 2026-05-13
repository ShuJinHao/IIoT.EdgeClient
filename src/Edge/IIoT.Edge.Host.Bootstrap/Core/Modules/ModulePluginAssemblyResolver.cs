using System.Reflection;
using System.IO;
using System.Runtime.Loader;

namespace IIoT.Edge.Host.Bootstrap.Modules;

public interface IModulePluginAssemblyResolver
{
    Assembly LoadAssembly(string assemblyPath, string pluginDirectory);
}

public sealed class ModulePluginAssemblyResolver : IModulePluginAssemblyResolver, IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, string> _assemblyPaths = new(StringComparer.OrdinalIgnoreCase);
    private int _initialized;
    private int _disposed;

    public Assembly LoadAssembly(string assemblyPath, string pluginDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        EnsureInitialized();
        RegisterPluginDirectory(pluginDirectory);

        var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(x => string.Equals(
                x.GetName().Name,
                assemblyName.Name,
                StringComparison.OrdinalIgnoreCase));

        if (loaded is not null)
        {
            return loaded;
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += Resolve;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveLegacy;
    }

    private void RegisterPluginDirectory(string pluginDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        lock (_sync)
        {
            foreach (var dllPath in Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(dllPath);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _assemblyPaths[name] = dllPath;
                }
            }
        }
    }

    private Assembly? Resolve(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (_disposed != 0)
        {
            return null;
        }

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(x => string.Equals(
                x.GetName().Name,
                assemblyName.Name,
                StringComparison.OrdinalIgnoreCase));

        if (loaded is not null)
        {
            return loaded;
        }

        lock (_sync)
        {
            if (assemblyName.Name is null)
            {
                return null;
            }

            if (!_assemblyPaths.TryGetValue(assemblyName.Name, out var path))
            {
                return null;
            }

            if (!File.Exists(path))
            {
                return null;
            }

            return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
    }

    private Assembly? ResolveLegacy(object? sender, ResolveEventArgs args)
    {
        if (_disposed != 0)
        {
            return null;
        }

        var requested = new AssemblyName(args.Name);
        return Resolve(AssemblyLoadContext.Default, requested);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_initialized != 0)
        {
            AssemblyLoadContext.Default.Resolving -= Resolve;
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveLegacy;
        }
    }
}
