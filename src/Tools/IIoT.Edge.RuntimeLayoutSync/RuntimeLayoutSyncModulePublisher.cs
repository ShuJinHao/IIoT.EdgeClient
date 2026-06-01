using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

internal sealed class RuntimeLayoutSyncModulePublisher(IRuntimeLayoutSyncFileSystem fileSystem) : IRuntimeLayoutSyncModulePublisher
{
    public void PublishModulesToRuntimeRoot(
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

        fileSystem.RemoveDirectoryIfExists(targetModulesRoot);
        fileSystem.CreateDirectory(targetModulesRoot);

        foreach (var moduleId in uniqueModuleIds)
        {
            var project = moduleMap[moduleId];
            var moduleBuildRoot = Path.Combine(project.ProjectDirectory, "bin", configuration, project.TargetFramework);
            if (!fileSystem.DirectoryExists(moduleBuildRoot))
            {
                throw new DirectoryNotFoundException($"Module build output was not found: {moduleBuildRoot}");
            }

            var moduleRuntimeDirectory = Path.Combine(targetModulesRoot, moduleId);
            fileSystem.RemoveDirectoryIfExists(moduleRuntimeDirectory);
            fileSystem.CreateDirectory(moduleRuntimeDirectory);
            fileSystem.CopyDirectoryContent(moduleBuildRoot, moduleRuntimeDirectory);
            ValidatePluginManifest(Path.Combine(moduleRuntimeDirectory, "plugin.json"));
        }
    }

    private static Dictionary<string, ModuleProject> GetModuleProjectMap(string repoRoot)
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

    private static string? GetProjectProperty(XDocument project, string propertyName)
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

    private static void BuildModuleProject(string projectPath, string configuration)
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

    private void ValidatePluginManifest(string manifestPath)
    {
        var manifest = fileSystem.LoadJson<Dictionary<string, JsonElement>>(manifestPath)
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
}
