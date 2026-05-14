using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using IIoT.Edge.AvaloniaShell;
using IIoT.Edge.Host.Bootstrap.Avalonia;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class AvaloniaPublishLayoutPreflightTests
{
    [Fact]
    public void LauncherAvaloniaProfiles_ShouldExposeUiOnlyAndRuntimeEntries()
    {
        var root = FindRepositoryRoot();
        var profilePath = Path.Combine(root, "src", "Edge", "IIoT.Edge.Launcher.Avalonia", "launcher.profiles.json");
        using var document = JsonDocument.Parse(File.ReadAllText(profilePath));

        var profiles = document.RootElement.EnumerateArray().ToArray();

        Assert.Equal(2, profiles.Length);
        Assert.All(
            profiles,
            profile =>
            {
                Assert.Equal("..\\avalonia-shell\\IIoT.Edge.AvaloniaShell.exe", profile.GetProperty("ExecutablePath").GetString());
                Assert.Contains("Avalonia", profile.GetProperty("DisplayName").GetString(), StringComparison.Ordinal);
            });

        var uiOnly = Assert.Single(profiles, profile => profile.GetProperty("ProfileId").GetString() == "HomogenizationLineAvalonia");
        Assert.Contains("UI-only", uiOnly.GetProperty("DisplayName").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(uiOnly.TryGetProperty("Arguments", out _));

        var runtime = Assert.Single(profiles, profile => profile.GetProperty("ProfileId").GetString() == "HomogenizationLineAvaloniaRuntime");
        Assert.Contains(
            "--start-runtime",
            runtime.GetProperty("Arguments").EnumerateArray().Select(static item => item.GetString()));
    }

    [Fact]
    public void AvaloniaShellProject_ShouldCopyAvaloniaPluginArtifactsToBuildAndPublishLayout()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "src", "Edge", "IIoT.Edge.AvaloniaShell", "IIoT.Edge.AvaloniaShell.csproj");
        var document = XDocument.Load(projectPath);
        var targetNames = document.Descendants("Target")
            .Select(element => element.Attribute("Name")?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ResolveAvaloniaPluginModuleArtifacts", targetNames);
        Assert.Contains("CopyAvaloniaPluginModulesToBuildOutput", targetNames);
        Assert.Contains("CopyAvaloniaPluginModulesToPublishOutput", targetNames);

        var pluginProjectInclude = document.Descendants("AvaloniaPluginModuleProject")
            .Select(element => element.Attribute("Include")?.Value)
            .SingleOrDefault();

        Assert.Equal("..\\..\\Modules\\**\\*.Avalonia.csproj", pluginProjectInclude);
        Assert.Contains(
            document.Descendants("Copy"),
            element => (element.Attribute("DestinationFiles")?.Value ?? string.Empty)
                .Contains("Modules\\%(ModuleId)", StringComparison.Ordinal));
    }

    [Fact]
    public void LauncherAvaloniaProject_ShouldPublishProfilesAndAccountsSample()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "src", "Edge", "IIoT.Edge.Launcher.Avalonia", "IIoT.Edge.Launcher.Avalonia.csproj");
        var document = XDocument.Load(projectPath);

        AssertHasCopyMetadata(document, "launcher.profiles.json", "CopyToOutputDirectory", "Always");
        AssertHasCopyMetadata(document, "launcher.profiles.json", "CopyToPublishDirectory", "Always");
        AssertHasCopyMetadata(document, "launcher.accounts.sample.json", "CopyToPublishDirectory", "PreserveNewest");
    }

    [Fact]
    public void HomogenizationAvaloniaPlugin_ShouldPublishManifestAndModuleConfig()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(root, "src", "Modules", "IIoT.Edge.Module.Homogenization.Avalonia", "IIoT.Edge.Module.Homogenization.Avalonia.csproj");
        var document = XDocument.Load(projectPath);

        AssertHasCopyMetadata(document, "plugin.json", "CopyToOutputDirectory", "PreserveNewest");
        AssertHasCopyMetadata(document, "plugin.json", "CopyToPublishDirectory", "PreserveNewest");
        Assert.Contains(
            document.Descendants("None"),
            element =>
                (element.Attribute("Include")?.Value ?? string.Empty).Contains("homogenization.module.json", StringComparison.Ordinal) &&
                element.Elements("CopyToPublishDirectory").Any(child => child.Value == "PreserveNewest"));
    }

    [Fact]
    public void PublishAvaloniaMigrationScript_ShouldStayAvaloniaOnlyAndGenerateReleaseManifest()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "PublishAvaloniaMigration.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("IIoT.Edge.Launcher.Avalonia", script, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.AvaloniaShell", script, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.Module.Homogenization.Avalonia", script, StringComparison.Ordinal);
        Assert.Contains("release-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("Avalonia12-现场联调检查清单.md", script, StringComparison.Ordinal);
        Assert.Contains("NuGet预览传递依赖例外记录.md", script, StringComparison.Ordinal);
        Assert.Contains("switchMatrixRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("switchBlockerRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("trialManualRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("trialAcceptanceTemplateRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("trialIssueRecoveryRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("defaultEntryDecisionTemplateRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("StartAvaloniaTrialRun.ps1", script, StringComparison.Ordinal);
        Assert.Contains("ReviewAvaloniaTrialEvidence.ps1", script, StringComparison.Ordinal);
        Assert.Contains("SkiaSharp", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishEdgeRuntime.ps1", script, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishEdgeBundle.ps1", script, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Edge\\IIoT.Edge.Launcher\\IIoT.Edge.Launcher.csproj", script, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Edge\\IIoT.Edge.Shell\\IIoT.Edge.Shell.csproj", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaShellBuildOutput_ShouldContainHomogenizationPluginModuleLayout()
    {
        var root = FindRepositoryRoot();
        var publishRoot = GetAvaloniaPublishRoot(root, "avalonia-shell");
        var moduleRoot = Path.Combine(publishRoot, "Modules", "Homogenization");

        AssertRequiredFile(publishRoot, "IIoT.Edge.AvaloniaShell.exe");
        Assert.False(File.Exists(Path.Combine(publishRoot, "plugin.json")), "Plugin manifest must stay under Modules/{ModuleId}, not shell root.");
        AssertRequiredFile(moduleRoot, "plugin.json");
        AssertRequiredFile(moduleRoot, "IIoT.Edge.Module.Homogenization.Avalonia.dll");
        AssertRequiredFile(moduleRoot, Path.Combine("Config", "homogenization.module.json"));
    }

    [Fact]
    public void AvaloniaReleaseCandidateOutputs_ShouldContainLauncherShellPluginAndRuntimeTemplate()
    {
        var root = FindRepositoryRoot();
        var configuration = ResolveCurrentConfiguration();
        var launcherRoot = GetAvaloniaPublishRoot(root, "avalonia-launcher");
        var shellRoot = GetAvaloniaPublishRoot(root, "avalonia-shell");
        var shellModuleRoot = Path.Combine(shellRoot, "Modules", "Homogenization");
        var moduleOutputRoot = Path.Combine(
            root,
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization.Avalonia",
            "bin",
            configuration,
            "net10.0");

        AssertRequiredFile(launcherRoot, "IIoT.Edge.Launcher.Avalonia.exe");
        AssertRequiredFile(launcherRoot, "launcher.profiles.json");
        AssertRequiredFile(launcherRoot, Path.Combine("Assets", "Profiles", "homogenization.png"));

        AssertRequiredFile(shellRoot, "IIoT.Edge.AvaloniaShell.exe");
        AssertRequiredFile(shellModuleRoot, "plugin.json");
        AssertRequiredFile(shellModuleRoot, "IIoT.Edge.Module.Homogenization.Avalonia.dll");
        AssertRequiredFile(shellModuleRoot, Path.Combine("Config", "homogenization.module.json"));

        AssertRequiredFile(moduleOutputRoot, "plugin.json");
        AssertRequiredFile(moduleOutputRoot, "IIoT.Edge.Module.Homogenization.Avalonia.dll");
        AssertRequiredFile(moduleOutputRoot, Path.Combine("Config", "homogenization.module.json"));

        using var profiles = JsonDocument.Parse(File.ReadAllText(Path.Combine(launcherRoot, "launcher.profiles.json")));
        var profileArray = profiles.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, profileArray.Length);

        foreach (var profile in profileArray)
        {
            var executablePath = profile.GetProperty("ExecutablePath").GetString();
            Assert.False(string.IsNullOrWhiteSpace(executablePath));
            var resolvedExecutable = Path.GetFullPath(Path.Combine(launcherRoot, executablePath!));
            Assert.True(File.Exists(resolvedExecutable), resolvedExecutable);
        }

        var uiOnly = Assert.Single(profileArray, profile => profile.GetProperty("ProfileId").GetString() == "HomogenizationLineAvalonia");
        Assert.False(uiOnly.TryGetProperty("Arguments", out _));

        var runtime = Assert.Single(profileArray, profile => profile.GetProperty("ProfileId").GetString() == "HomogenizationLineAvaloniaRuntime");
        Assert.Contains(
            "--start-runtime",
            runtime.GetProperty("Arguments").EnumerateArray().Select(static item => item.GetString()));

        AssertAvaloniaRuntimeDirectoryTemplate();
    }

    private static void AssertHasCopyMetadata(
        XDocument document,
        string itemPath,
        string metadataName,
        string expectedValue)
    {
        Assert.Contains(
            document.Descendants("None"),
            element =>
                string.Equals(element.Attribute("Update")?.Value, itemPath, StringComparison.OrdinalIgnoreCase) &&
                element.Elements(metadataName).Any(child => child.Value == expectedValue));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Cannot locate IIoT.EdgeClient.AvaloniaMigration repository root.");
    }

    private static string GetAvaloniaPublishRoot(string root, string outputDirectory)
        => Path.GetFullPath(Path.Combine(root, "..", "publish", ResolveCurrentConfiguration(), outputDirectory));

    private static string ResolveCurrentConfiguration()
    {
        var segments = AppContext.BaseDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (segments[index].Equals("bin", StringComparison.OrdinalIgnoreCase))
            {
                return segments[index + 1];
            }
        }

        return "Debug";
    }

    private static void AssertRequiredFile(string root, string relativePath)
    {
        var fullPath = Path.Combine(root, relativePath);
        Assert.True(File.Exists(fullPath), fullPath);
    }

    private static void AssertAvaloniaRuntimeDirectoryTemplate()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "iiot-avalonia-runtime-template");
        Directory.CreateDirectory(baseDirectory);
        var method = typeof(App).GetMethod("CreateBootstrapOptions", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var options = Assert.IsType<AvaloniaHostBootstrapOptions>(method.Invoke(null, [baseDirectory]));
        var runtimeRoot = Path.Combine(baseDirectory, "data", "avalonia-migration");
        var pluginDirectories = options.PluginDirectories ?? Array.Empty<string>();

        Assert.Equal("AvaloniaMigration", options.EnvironmentName);
        Assert.Equal(new[] { "Homogenization" }, options.ModuleIds);
        Assert.Equal(runtimeRoot, options.RuntimePaths.RuntimeDataRoot);
        Assert.Equal(Path.Combine(runtimeRoot, "db"), options.RuntimePaths.DatabaseDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "context"), options.RuntimePaths.ContextDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "recipe"), options.RuntimePaths.RecipeDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "excel"), options.RuntimePaths.ExcelDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "diagnostics"), options.RuntimePaths.DiagnosticsDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "diagnostics", "logs"), options.RuntimePaths.LogDirectory);
        Assert.Equal(Path.Combine(runtimeRoot, "device_cache.json"), options.RuntimePaths.DeviceCacheFilePath);
        Assert.Contains(baseDirectory, pluginDirectories);
    }
}
