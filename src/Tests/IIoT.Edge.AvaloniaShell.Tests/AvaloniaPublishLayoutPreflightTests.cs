using System.Text.Json;
using System.Xml.Linq;
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
    public void AvaloniaShellBuildOutput_ShouldContainHomogenizationPluginModuleLayout()
    {
        var root = FindRepositoryRoot();
        var publishRoot = Path.GetFullPath(Path.Combine(root, "..", "publish", "Debug", "avalonia-shell"));
        var moduleRoot = Path.Combine(publishRoot, "Modules", "Homogenization");

        Assert.True(File.Exists(Path.Combine(publishRoot, "IIoT.Edge.AvaloniaShell.exe")), publishRoot);
        Assert.False(File.Exists(Path.Combine(publishRoot, "plugin.json")), "插件 manifest 不能落在 Shell 根目录，避免 catalog 扫到重复模块。");
        Assert.True(File.Exists(Path.Combine(moduleRoot, "plugin.json")), moduleRoot);
        Assert.True(File.Exists(Path.Combine(moduleRoot, "IIoT.Edge.Module.Homogenization.Avalonia.dll")), moduleRoot);
        Assert.True(File.Exists(Path.Combine(moduleRoot, "Config", "homogenization.module.json")), moduleRoot);
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
}
