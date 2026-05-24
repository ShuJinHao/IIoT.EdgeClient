using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

var options = CommandLineOptions.Parse(args);
var repoRoot = ResolvePath(Environment.CurrentDirectory, options.RepoRoot);
var manifestPath = ResolvePath(repoRoot, options.ManifestPath);
var profileCatalogPath = ResolvePath(repoRoot, options.LauncherProfileCatalogPath);
var layoutRoot = ResolvePath(repoRoot, options.LayoutRoot);
var launcherRuntimeRoot = ResolvePath(repoRoot, options.LauncherRuntimeRoot);
var shellRuntimeRoot = ResolvePath(repoRoot, options.ShellRuntimeRoot);

var manifest = LoadJson<RuntimePublishManifest>(manifestPath)
    ?? throw new InvalidOperationException($"Edge runtime publish manifest '{manifestPath}' could not be parsed.");
var profiles = LoadJson<List<LauncherProfileEntry>>(profileCatalogPath)
    ?? throw new InvalidOperationException($"Launcher profile catalog '{profileCatalogPath}' could not be parsed.");

ValidateManifest(manifest, manifestPath);
ValidateProfilesMatchManifest(manifest, profiles, launcherRuntimeRoot, checkExecutablePath: false);

if (!Directory.Exists(launcherRuntimeRoot))
{
    throw new DirectoryNotFoundException($"Launcher build output was not found: {launcherRuntimeRoot}");
}

if (!Directory.Exists(shellRuntimeRoot))
{
    throw new DirectoryNotFoundException($"Shell build output was not found: {shellRuntimeRoot}");
}

CopyFile(profileCatalogPath, Path.Combine(launcherRuntimeRoot, "launcher.profiles.json"));
RemoveLauncherShellArtifacts(launcherRuntimeRoot);

foreach (var runtime in manifest.Runtimes)
{
    SyncProcessRuntime(repoRoot, options.Configuration, shellRuntimeRoot, runtime, layoutRoot);
}

ValidateProfilesMatchManifest(manifest, profiles, launcherRuntimeRoot, checkExecutablePath: true);
Console.WriteLine($"Synchronized local runtime layout: {layoutRoot}");

static T? LoadJson<T>(string path)
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

static void ValidateManifest(RuntimePublishManifest manifest, string manifestPath)
{
    if (string.IsNullOrWhiteSpace(manifest.LauncherDirectory))
    {
        throw new InvalidOperationException($"Edge runtime publish manifest '{manifestPath}' is missing launcherDirectory.");
    }

    if (manifest.Runtimes.Count == 0)
    {
        throw new InvalidOperationException($"Edge runtime publish manifest '{manifestPath}' does not contain any runtimes.");
    }

    var runtimeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var outputDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var runtime in manifest.Runtimes)
    {
        foreach (var (name, value) in new[]
                 {
                     ("runtimeId", runtime.RuntimeId),
                     ("profileId", runtime.ProfileId),
                     ("machineProfile", runtime.MachineProfile),
                     ("outputDirectory", runtime.OutputDirectory),
                     ("machineConfig", runtime.MachineConfig)
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Runtime entry in '{manifestPath}' is missing {name}.");
            }
        }

        if (runtime.ModuleIds.Count == 0)
        {
            throw new InvalidOperationException($"Runtime entry '{runtime.RuntimeId}' in '{manifestPath}' does not define moduleIds.");
        }

        if (!runtimeIds.Add(runtime.RuntimeId))
        {
            throw new InvalidOperationException($"Runtime id '{runtime.RuntimeId}' is duplicated in '{manifestPath}'.");
        }

        if (!outputDirectories.Add(runtime.OutputDirectory))
        {
            throw new InvalidOperationException($"Runtime outputDirectory '{runtime.OutputDirectory}' is duplicated in '{manifestPath}'.");
        }

        if (!profileIds.Add(runtime.ProfileId))
        {
            throw new InvalidOperationException($"Runtime profileId '{runtime.ProfileId}' is duplicated in '{manifestPath}'.");
        }
    }
}

static void ValidateProfilesMatchManifest(
    RuntimePublishManifest manifest,
    IReadOnlyList<LauncherProfileEntry> profiles,
    string launcherRuntimeRoot,
    bool checkExecutablePath)
{
    if (profiles.Count == 0)
    {
        throw new InvalidOperationException("Launcher profile catalog is empty.");
    }

    var runtimeByProfileId = manifest.Runtimes.ToDictionary(runtime => runtime.ProfileId, StringComparer.OrdinalIgnoreCase);
    var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var profile in profiles)
    {
        foreach (var (name, value) in new[]
                 {
                     ("ProfileId", profile.ProfileId),
                     ("DisplayName", profile.DisplayName),
                     ("MachineProfile", profile.MachineProfile),
                     ("ExecutablePath", profile.ExecutablePath)
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Launcher profile catalog contains a profile missing {name}.");
            }
        }

        profileIds.Add(profile.ProfileId);
        if (!runtimeByProfileId.TryGetValue(profile.ProfileId, out var runtime))
        {
            throw new InvalidOperationException($"Launcher profile '{profile.ProfileId}' does not match any runtime profileId in edge-runtime.publish.json.");
        }

        if (!string.Equals(profile.MachineProfile, runtime.MachineProfile, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Launcher profile '{profile.ProfileId}' machineProfile '{profile.MachineProfile}' does not match runtime machineProfile '{runtime.MachineProfile}'.");
        }

        if (checkExecutablePath)
        {
            var configuredPath = ResolvePath(launcherRuntimeRoot, profile.ExecutablePath);
            var candidates = GetExecutableCandidates(configuredPath, OperatingSystem.IsWindows()).ToArray();
            if (!candidates.Any(File.Exists))
            {
                throw new FileNotFoundException(
                    $"Launcher profile '{profile.ProfileId}' points to a missing executable: {configuredPath}. Candidates: {string.Join(", ", candidates)}",
                    configuredPath);
            }
        }
    }

    foreach (var runtime in manifest.Runtimes)
    {
        if (!profileIds.Contains(runtime.ProfileId))
        {
            throw new InvalidOperationException($"Runtime '{runtime.RuntimeId}' profileId '{runtime.ProfileId}' is missing from launcher.profiles.json.");
        }
    }
}

static void SyncProcessRuntime(
    string repoRoot,
    string configuration,
    string shellRuntimeSource,
    RuntimeDefinition runtime,
    string layoutRoot)
{
    var runtimeRoot = Path.Combine(layoutRoot, NormalizePathSeparators(runtime.OutputDirectory));
    CleanDirectory(runtimeRoot);
    CopyDirectoryContent(shellRuntimeSource, runtimeRoot);

    foreach (var file in Directory.EnumerateFiles(runtimeRoot, "appsettings.machine.*.json", SearchOption.TopDirectoryOnly))
    {
        File.Delete(file);
    }

    var machineConfigSource = ResolvePath(repoRoot, runtime.MachineConfig);
    if (!File.Exists(machineConfigSource))
    {
        throw new FileNotFoundException($"Machine profile config was not found for runtime '{runtime.RuntimeId}': {machineConfigSource}", machineConfigSource);
    }

    CopyFile(machineConfigSource, Path.Combine(runtimeRoot, Path.GetFileName(machineConfigSource)));

    var modulesRoot = Path.Combine(runtimeRoot, "Modules");
    PublishModulesToRuntimeRoot(repoRoot, configuration, runtime.ModuleIds, modulesRoot);

    var shellCandidates = GetShellExecutableCandidates(runtimeRoot).ToArray();
    if (!shellCandidates.Any(File.Exists))
    {
        throw new FileNotFoundException(
            $"Shell executable was not found in runtime directory: {runtimeRoot}. Candidates: {string.Join(", ", shellCandidates)}",
            runtimeRoot);
    }
}

static void PublishModulesToRuntimeRoot(
    string repoRoot,
    string configuration,
    IReadOnlyList<string> moduleIds,
    string targetModulesRoot)
{
    var moduleMap = GetModuleProjectMap(repoRoot);
    var uniqueModuleIds = moduleIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    foreach (var moduleId in uniqueModuleIds)
    {
        if (!moduleMap.TryGetValue(moduleId, out var project))
        {
            throw new InvalidOperationException($"Module '{moduleId}' was not found under src/Modules.");
        }

        BuildModuleProject(project.ProjectPath, configuration);
    }

    RemoveDirectoryIfExists(targetModulesRoot);
    Directory.CreateDirectory(targetModulesRoot);

    foreach (var moduleId in uniqueModuleIds)
    {
        var project = moduleMap[moduleId];
        var moduleBuildRoot = Path.Combine(project.ProjectDirectory, "bin", configuration, project.TargetFramework);
        if (!Directory.Exists(moduleBuildRoot))
        {
            throw new DirectoryNotFoundException($"Module build output was not found: {moduleBuildRoot}");
        }

        var moduleRuntimeDirectory = Path.Combine(targetModulesRoot, moduleId);
        RemoveDirectoryIfExists(moduleRuntimeDirectory);
        Directory.CreateDirectory(moduleRuntimeDirectory);
        CopyDirectoryContent(moduleBuildRoot, moduleRuntimeDirectory);
        ValidatePluginManifest(Path.Combine(moduleRuntimeDirectory, "plugin.json"));
    }
}

static Dictionary<string, ModuleProject> GetModuleProjectMap(string repoRoot)
{
    var modulesRoot = Path.Combine(repoRoot, "src", "Modules");
    var map = new Dictionary<string, ModuleProject>(StringComparer.OrdinalIgnoreCase);

    foreach (var projectPath in Directory.EnumerateFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories))
    {
        var project = XDocument.Load(projectPath);
        var moduleId = GetProjectProperty(project, "PluginModuleId");
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            continue;
        }

        var targetFramework = GetProjectProperty(project, "TargetFramework");
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            throw new InvalidOperationException($"Module project '{projectPath}' is missing TargetFramework.");
        }

        map[moduleId] = new ModuleProject(moduleId, projectPath, Path.GetDirectoryName(projectPath)!, targetFramework);
    }

    return map;
}

static string? GetProjectProperty(XDocument project, string propertyName)
{
    foreach (var propertyGroup in project.Root?.Elements("PropertyGroup") ?? [])
    {
        var value = propertyGroup.Element(propertyName)?.Value;
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    return null;
}

static void BuildModuleProject(string projectPath, string configuration)
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    foreach (var argument in new[]
             {
                 "build",
                 projectPath,
                 "--configuration",
                 configuration,
                 "--nologo",
                 "--verbosity",
                 "minimal",
                 "--disable-build-servers",
                 "-p:BuildInParallel=false",
                 "-p:RestoreDisableParallel=true"
             })
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start dotnet build for module project: {projectPath}");
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (!string.IsNullOrWhiteSpace(output))
    {
        Console.Write(output);
    }

    if (!string.IsNullOrWhiteSpace(error))
    {
        Console.Error.Write(error);
    }

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"dotnet build failed for module project '{projectPath}' with exit code {process.ExitCode}.");
    }
}

static void ValidatePluginManifest(string manifestPath)
{
    var manifest = LoadJson<Dictionary<string, JsonElement>>(manifestPath)
        ?? throw new InvalidOperationException($"Plugin manifest '{manifestPath}' could not be parsed.");

    foreach (var property in new[]
             {
                 "moduleId",
                 "displayName",
                 "version",
                 "hostApiVersion",
                 "minHostVersion",
                 "maxHostVersion",
                 "entryAssembly",
                 "entryType",
                 "supportedProcessType"
             })
    {
        if (!manifest.TryGetValue(property, out var value) || value.ValueKind == JsonValueKind.Null ||
            (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())))
        {
            throw new InvalidOperationException($"Plugin manifest '{manifestPath}' is missing {property}.");
        }
    }
}

static void RemoveLauncherShellArtifacts(string launcherRuntimeRoot)
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

static IEnumerable<string> GetShellExecutableCandidates(string runtimeRoot)
{
    return GetExecutableCandidates(
        Path.Combine(runtimeRoot, "IIoT.Edge.Shell"),
        OperatingSystem.IsWindows());
}

static IReadOnlyList<string> GetExecutableCandidates(string configuredPath, bool isWindows)
{
    var candidates = new List<string>();
    var hasExeExtension = HasKnownExtension(configuredPath, ".exe");
    var hasDllExtension = HasKnownExtension(configuredPath, ".dll");

    if (isWindows && !hasExeExtension && !hasDllExtension)
    {
        AddDistinct(candidates, configuredPath + ".exe");
    }
    else if (!isWindows && hasExeExtension)
    {
        AddDistinct(candidates, RemoveKnownExtension(configuredPath, ".exe"));
    }

    if (!hasDllExtension)
    {
        AddDistinct(candidates, configuredPath);
    }

    AddDistinct(candidates, GetDllFallbackPath(configuredPath));
    return candidates;
}

static string GetDllFallbackPath(string configuredPath)
{
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

static bool HasKnownExtension(string path, string extension)
    => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

static string RemoveKnownExtension(string path, string extension)
{
    var directory = Path.GetDirectoryName(path) ?? string.Empty;
    var fileName = Path.GetFileName(path);
    var trimmedFileName = fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
        ? fileName[..^extension.Length]
        : fileName;
    return Path.Combine(directory, trimmedFileName);
}

static void AddDistinct(List<string> candidates, string path)
{
    if (!candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
    {
        candidates.Add(path);
    }
}

static void CopyDirectoryContent(string sourceDirectory, string targetDirectory)
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

static void CopyFile(string sourcePath, string targetPath)
{
    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
    File.Copy(sourcePath, targetPath, overwrite: true);
    PreserveUnixExecutableMode(sourcePath, targetPath);
}

static void PreserveUnixExecutableMode(string sourcePath, string targetPath)
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

static void CleanDirectory(string directory)
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

static void DeleteFileIfExists(string path)
{
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}

static void RemoveDirectoryIfExists(string path)
{
    if (Directory.Exists(path))
    {
        Directory.Delete(path, recursive: true);
    }
}

static string ResolvePath(string basePath, string pathValue)
{
    var normalized = NormalizePathSeparators(Environment.ExpandEnvironmentVariables(pathValue.Trim()));
    return Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(basePath, normalized));
}

static string NormalizePathSeparators(string path)
    => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

internal sealed record RuntimePublishManifest(
    string LauncherDirectory,
    List<RuntimeDefinition> Runtimes);

internal sealed record RuntimeDefinition(
    string RuntimeId,
    string ProfileId,
    string MachineProfile,
    string OutputDirectory,
    string MachineConfig,
    List<string> ModuleIds);

internal sealed record LauncherProfileEntry(
    string ProfileId,
    string DisplayName,
    string MachineProfile,
    string ExecutablePath);

internal sealed record ModuleProject(
    string ModuleId,
    string ProjectPath,
    string ProjectDirectory,
    string TargetFramework);

internal sealed record CommandLineOptions(
    string Configuration,
    string RepoRoot,
    string ManifestPath,
    string LauncherProfileCatalogPath,
    string LayoutRoot,
    string LauncherRuntimeRoot,
    string ShellRuntimeRoot)
{
    public static CommandLineOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument: {key}");
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for argument: {key}");
            }

            values[key[2..]] = args[++index];
        }

        return new CommandLineOptions(
            Get(values, "configuration", "Debug"),
            Get(values, "repo-root", Directory.GetCurrentDirectory()),
            Get(values, "manifest-path", "scripts/edge-runtime.publish.json"),
            Get(values, "launcher-profile-catalog-path", "src/Edge/IIoT.Edge.Launcher/launcher.profiles.json"),
            Get(values, "layout-root", "../publish/Debug"),
            Get(values, "launcher-runtime-root", "../publish/Debug/launcher"),
            Get(values, "shell-runtime-root", "../publish/Debug/shell"));
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string defaultValue)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
}
