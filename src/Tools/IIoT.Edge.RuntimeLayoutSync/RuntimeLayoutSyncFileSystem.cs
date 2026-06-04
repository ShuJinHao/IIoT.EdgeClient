using System.Text.Json;

internal sealed class RuntimeLayoutSyncFileSystem : IRuntimeLayoutSyncFileSystem
{
    public void RemoveLauncherShellArtifacts(string launcherRuntimeRoot)
    {
        foreach (var file in Directory.EnumerateFiles(launcherRuntimeRoot, "appsettings*.json", SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
        }

        foreach (var fileName in new[]
                 {
                     "IIoT.Edge.Shell",
                     "IIoT.Edge.Shell.exe",
                     "IIoT.Edge.Shell.dll",
                     "IIoT.Edge.Shell.deps.json",
                     "IIoT.Edge.Shell.runtimeconfig.json",
                     "IIoT.Edge.Shell.pdb",
                     "IIoT.Edge.Application.dll",
                     "IIoT.Edge.Application.pdb",
                     "IIoT.Edge.Domain.dll",
                     "IIoT.Edge.Domain.pdb",
                     "IIoT.Edge.Host.Bootstrap.dll",
                     "IIoT.Edge.Host.Bootstrap.pdb",
                     "IIoT.Edge.Infrastructure.DeviceComm.dll",
                     "IIoT.Edge.Infrastructure.DeviceComm.pdb",
                     "IIoT.Edge.Infrastructure.Integration.dll",
                     "IIoT.Edge.Infrastructure.Integration.pdb",
                     "IIoT.Edge.Infrastructure.Persistence.Dapper.dll",
                     "IIoT.Edge.Infrastructure.Persistence.Dapper.pdb",
                     "IIoT.Edge.Infrastructure.Persistence.EfCore.dll",
                     "IIoT.Edge.Infrastructure.Persistence.EfCore.pdb",
                     "IIoT.Edge.Presentation.Navigation.dll",
                     "IIoT.Edge.Presentation.Navigation.pdb",
                     "IIoT.Edge.Presentation.Panels.dll",
                     "IIoT.Edge.Presentation.Panels.pdb",
                     "IIoT.Edge.Presentation.Shell.dll",
                     "IIoT.Edge.Presentation.Shell.pdb",
                     "IIoT.Edge.Presentation.VisualTestData.dll",
                     "IIoT.Edge.Presentation.VisualTestData.pdb",
                     "IIoT.Edge.Runtime.dll",
                     "IIoT.Edge.Runtime.pdb",
                     "log4net.config"
                 })
        {
            DeleteFileIfExists(Path.Combine(launcherRuntimeRoot, fileName));
        }

        foreach (var directoryName in new[] { "Modules", "Logs", "data" })
        {
            RemoveDirectoryIfExists(Path.Combine(launcherRuntimeRoot, directoryName));
        }
    }

    public void CopyDirectoryContent(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory was not found: {sourceDirectory}");
        }

        Directory.CreateDirectory(targetDirectory);

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            CopyFile(sourcePath, targetPath);
        }
    }

    public void CopyFile(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: true);
        PreserveUnixExecutableMode(sourcePath, targetPath);
    }

    public void CleanDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    public void CreateDirectory(string directory)
        => Directory.CreateDirectory(directory);

    public void RemoveDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public void DeleteFiles(string directory, string searchPattern, SearchOption searchOption)
    {
        foreach (var file in Directory.EnumerateFiles(directory, searchPattern, searchOption))
        {
            File.Delete(file);
        }
    }

    public void EnsureDirectoryExists(string directory, string message)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"{message}: {directory}");
        }
    }

    public bool FileExists(string path)
        => File.Exists(path);

    public bool DirectoryExists(string path)
        => Directory.Exists(path);

    public T? LoadJson<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"JSON file was not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        });
    }

    public string ResolvePath(string basePath, string pathValue)
    {
        var normalized = NormalizePathSeparators(Environment.ExpandEnvironmentVariables(pathValue.Trim()));
        return Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(basePath, normalized));
    }

    public string NormalizePathSeparators(string path)
        => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static void PreserveUnixExecutableMode(string sourcePath, string targetPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var sourceMode = File.GetUnixFileMode(sourcePath);
            if ((sourceMode & UnixFileMode.UserExecute) == UnixFileMode.UserExecute)
            {
                File.SetUnixFileMode(targetPath, sourceMode);
                return;
            }

            if (Path.GetFileName(targetPath) is "IIoT.Edge.Shell" or "IIoT.Edge.Launcher")
            {
                File.SetUnixFileMode(
                    targetPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
