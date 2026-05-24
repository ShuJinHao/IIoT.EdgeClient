using System.IO;

namespace IIoT.Edge.Launcher.Services;

internal static class ShellLaunchTargetResolver
{
    internal static ShellLaunchTarget Resolve(string configuredPath)
        => Resolve(configuredPath, OperatingSystem.IsWindows(), File.Exists);

    internal static ShellLaunchTarget Resolve(
        string configuredPath,
        bool isWindows,
        Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);
        ArgumentNullException.ThrowIfNull(fileExists);

        foreach (var candidate in GetDirectExecutableCandidates(configuredPath, isWindows))
        {
            if (fileExists(candidate))
            {
                return new ShellLaunchTarget(
                    candidate,
                    [],
                    Path.GetDirectoryName(candidate) ?? AppContext.BaseDirectory);
            }
        }

        var dllFallbackPath = GetDllFallbackPath(configuredPath);
        if (fileExists(dllFallbackPath))
        {
            return new ShellLaunchTarget(
                "dotnet",
                [dllFallbackPath],
                Path.GetDirectoryName(dllFallbackPath) ?? AppContext.BaseDirectory);
        }

        var candidates = GetExecutableCandidates(configuredPath, isWindows);
        throw new FileNotFoundException(
            $"未找到目标客户端可执行文件：{configuredPath}。候选路径：{string.Join(", ", candidates)}。请先确认目标工序运行目录已生成，或检查 launcher.profiles.json 中的 ExecutablePath 配置。",
            configuredPath);
    }

    internal static IReadOnlyList<string> GetExecutableCandidates(string configuredPath, bool isWindows)
    {
        var candidates = new List<string>();
        AddDistinct(candidates, GetDirectExecutableCandidates(configuredPath, isWindows));
        AddDistinct(candidates, GetDllFallbackPath(configuredPath));
        return candidates;
    }

    internal static IReadOnlyList<string> GetDirectExecutableCandidates(string configuredPath, bool isWindows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);

        var candidates = new List<string>();
        var hasExeExtension = HasKnownExtension(configuredPath, ".exe");
        var hasDllExtension = HasKnownExtension(configuredPath, ".dll");
        if (isWindows && !hasExeExtension && !hasDllExtension)
        {
            AddDistinct(candidates, configuredPath + ".exe");
        }
        else if (!isWindows && hasExeExtension)
        {
            AddDistinct(
                candidates,
                RemoveKnownExtension(configuredPath, ".exe"));
        }

        if (!hasDllExtension)
        {
            AddDistinct(candidates, configuredPath);
        }

        return candidates;
    }

    internal static string GetDllFallbackPath(string configuredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);

        if (HasKnownExtension(configuredPath, ".dll"))
        {
            return configuredPath;
        }

        if (HasKnownExtension(configuredPath, ".exe"))
        {
            return RemoveKnownExtension(configuredPath, ".exe") + ".dll";
        }

        return configuredPath + ".dll";
    }

    private static bool HasKnownExtension(string path, string extension)
        => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    private static string RemoveKnownExtension(string path, string extension)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileName(path);
        var trimmedFileName = fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^extension.Length]
            : fileName;
        return Path.Combine(directory, trimmedFileName);
    }

    private static void AddDistinct(List<string> candidates, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            AddDistinct(candidates, path);
        }
    }

    private static void AddDistinct(List<string> candidates, string path)
    {
        if (!candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(path);
        }
    }
}

internal sealed record ShellLaunchTarget(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);
