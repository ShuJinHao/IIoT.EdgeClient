internal interface IRuntimeLayoutSyncApp
{
    string Run(CommandLineOptions options);
}

internal interface IRuntimeLayoutSyncFileSystem
{
    void RemoveLauncherShellArtifacts(string launcherRuntimeRoot);

    void CopyDirectoryContent(string sourceDirectory, string targetDirectory);

    void CopyFile(string sourcePath, string targetPath);

    void CleanDirectory(string directory);

    void CreateDirectory(string directory);

    void RemoveDirectoryIfExists(string path);

    void DeleteFiles(string directory, string searchPattern, SearchOption searchOption);

    void EnsureDirectoryExists(string directory, string message);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    T? LoadJson<T>(string path);

    string ResolvePath(string basePath, string pathValue);

    string NormalizePathSeparators(string path);
}

internal interface IRuntimeLayoutSyncValidation
{
    void ValidateManifest(RuntimePublishManifest manifest, string manifestPath);

    void ValidateProfilesMatchManifest(
        RuntimePublishManifest manifest,
        IReadOnlyList<LauncherProfileEntry> profiles,
        string launcherRuntimeRoot,
        bool checkExecutablePath);

    IEnumerable<string> GetShellExecutableCandidates(string hostRoot);
}

internal interface IRuntimeLayoutSyncModulePublisher
{
    void PublishModulesToPluginsRoot(
        IReadOnlyList<string> moduleIds,
        string targetPluginsRoot);
}
