using System.Reflection;
using System.IO;
using System.Runtime.Loader;

namespace IIoT.Edge.Module.Abstractions;

internal static class ModulePluginAssemblyResolver
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, string> AssemblyPaths = new(StringComparer.OrdinalIgnoreCase);
    private static int _initialized;

    public static Assembly LoadAssembly(string assemblyPath, string pluginDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);

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

    private static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += Resolve;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveLegacy;
    }

    private static void RegisterPluginDirectory(string pluginDirectory)
    {
        lock (Sync)
        {
            foreach (var dllPath in Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileNameWithoutExtension(dllPath);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    AssemblyPaths[name] = dllPath;
                }
            }
        }
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(x => string.Equals(
                x.GetName().Name,
                assemblyName.Name,
                StringComparison.OrdinalIgnoreCase));

        if (loaded is not null)
        {
            return loaded;
        }

        lock (Sync)
        {
            if (assemblyName.Name is null)
            {
                return null;
            }

            if (!AssemblyPaths.TryGetValue(assemblyName.Name, out var path))
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

    private static Assembly? ResolveLegacy(object? sender, ResolveEventArgs args)
    {
        var requested = new AssemblyName(args.Name);
        return Resolve(AssemblyLoadContext.Default, requested);
    }
}
