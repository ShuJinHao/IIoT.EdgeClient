using System.Text.Json;
using IIoT.Edge.Shell.Core;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests;

public sealed class HomogenizationPackagingContractTests
{
    [Fact]
    public void SourceLayout_ShouldSeparateProductionRuntimeFromDevelopmentSamples()
    {
        var moduleRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization");

        Assert.True(File.Exists(Path.Combine(moduleRoot, "Production", "HomogenizationStationRuntimeFactory.cs")));
        Assert.False(File.Exists(Path.Combine(moduleRoot, "Production", "HomogenizationDevelopmentSampleContributor.cs")));
        Assert.True(File.Exists(Path.Combine(moduleRoot, "Samples", "HomogenizationDevelopmentSampleContributor.cs")));
        Assert.False(File.Exists(Path.Combine(moduleRoot, "Config", "HomogenizationDevelopmentSampleContributor.cs")));
    }

    [Fact]
    public void PluginBundle_ShouldSelectHomogenizationLineOnly()
    {
        var bundlePath = Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "PluginBundles",
            "homogenization-line.json");

        Assert.True(File.Exists(bundlePath));
        using var document = JsonDocument.Parse(File.ReadAllText(bundlePath));
        Assert.Equal("homogenization-line", document.RootElement.GetProperty("bundleId").GetString());
        Assert.Equal("Homogenization", document.RootElement.GetProperty("includeModules")[0].GetString());
        Assert.Equal("HomogenizationLine", document.RootElement.GetProperty("machineProfiles")[0].GetString());
    }

    [Fact]
    public void DataView_ShouldNotInjectVisualTestRowsFromUiConfig()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization",
            "Presentation",
            "HomogenizationNavigationViewModels.cs"));

        Assert.DoesNotContain("UI:VisualTestData:Enabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UI:VisualTestData:BatchCode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildVisualTestRows", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HomogenizationMachineProfile_ShouldStoreRuntimeDataUnderPublishRoot()
    {
        var repoRoot = FindRepositoryRoot();
        var machineProfilePath = Path.Combine(
            repoRoot,
            "src",
            "Edge",
            "IIoT.Edge.Shell",
            "appsettings.machine.HomogenizationLine.json");
        var shellOutputDirectory = Path.Combine(repoRoot, "publish", "Debug", "homogenization");
        var expectedRuntimeRoot = Path.Combine(
            repoRoot,
            "publish",
            "Debug",
            "data",
            "IIoT",
            "EdgeData",
            "profiles",
            "HomogenizationLine");

        var profileText = File.ReadAllText(machineProfilePath);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(machineProfilePath, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shell:MachineProfile"] = "HomogenizationLine"
            })
            .Build();

        var result = new ShellRuntimePathResolver().Resolve(shellOutputDirectory, configuration);

        Assert.DoesNotContain("%LocalAppData%", profileText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%ProgramData%", profileText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CommonApplicationData", profileText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("../data/profiles", profileText, StringComparison.Ordinal);
        Assert.DoesNotContain(@"..\\data\\profiles", profileText, StringComparison.Ordinal);
        Assert.Equal(expectedRuntimeRoot, result.RuntimeDataRoot);
        Assert.Equal(Path.Combine(expectedRuntimeRoot, "db"), result.DatabaseDirectory);
    }

    [Fact]
    public void ModuleDataPages_ShouldUseFillTableLayoutInsteadOfMinHeight()
    {
        var xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization",
            "Presentation",
            "Views",
            "HomogenizationDataPage.axaml"));

        Assert.Contains("<edge:EdgeTablePanel", xaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"fill\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewportMaxHeight=\"0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"620\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"520\"", xaml, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the IIoT.EdgeClient repository root.");
    }
}
