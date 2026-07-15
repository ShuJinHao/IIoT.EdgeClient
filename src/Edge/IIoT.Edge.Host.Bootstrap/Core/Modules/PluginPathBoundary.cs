namespace IIoT.Edge.Host.Bootstrap.Modules;

internal static class PluginPathBoundary
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static string ResolveExistingPhysicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException($"路径缺少根目录：{path}");
        var current = root;
        var relative = Path.GetRelativePath(root, fullPath);
        foreach (var component in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (!info.Exists)
                throw new FileNotFoundException("插件路径不存在。", current);

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
                current = Path.GetFullPath(target.FullName);
        }

        return Path.GetFullPath(current);
    }

    public static bool IsWithin(string physicalRoot, string physicalCandidate, bool allowSame = false)
    {
        var root = Path.GetFullPath(physicalRoot).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(physicalCandidate).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (allowSame && string.Equals(root, candidate, PathComparison))
            return true;

        return candidate.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
    }

    public static bool PathEquals(string first, string second)
        => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), PathComparison);
}
