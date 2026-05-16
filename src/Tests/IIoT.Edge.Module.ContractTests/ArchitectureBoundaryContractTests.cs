namespace IIoT.Edge.Module.ContractTests;

using System.Text.RegularExpressions;

public sealed class ArchitectureBoundaryContractTests
{
    private static readonly string[] ForbiddenModuleNamespaces =
    [
        "IIoT.Edge.Module.Homogenization"
    ];

    [Fact]
    public void HostAndCommonProjects_ShouldNotReferenceConcreteModuleNamespacesInCode()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var directories = new[]
        {
            Path.Combine(repoRoot, "src", "Application", "IIoT.Edge.Application"),
            Path.Combine(repoRoot, "src", "Runtime"),
            Path.Combine(repoRoot, "src", "Infrastructure", "IIoT.Edge.Infrastructure.Integration"),
            Path.Combine(repoRoot, "src", "Presentation"),
            Path.Combine(repoRoot, "src", "Shared", "IIoT.Edge.SharedKernel"),
            Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.AvaloniaShell"),
            Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.Host.Bootstrap"),
            Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.Host.Bootstrap")
        };

        var offendingFiles = directories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = path,
                Text = File.ReadAllText(path)
            })
            .Where(file => ForbiddenModuleNamespaces.Any(namespaceName => file.Text.Contains(namespaceName, StringComparison.Ordinal)))
            .Select(file => file.Path)
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void PluginCloudUploaders_ShouldDependOnApplicationCloudChannelAbstraction()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var cloudUploaderFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src", "Modules"), "*CloudUploader.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repoRoot, path),
                Text = File.ReadAllText(path)
            })
            .ToArray();

        Assert.NotEmpty(cloudUploaderFiles);
        var oldCloudBaseName = "Process" + "CloudUploaderBase";
        Assert.All(
            cloudUploaderFiles,
            file =>
            {
                Assert.Contains("CloudUploadChannelBase<", file.Text);
                Assert.DoesNotContain($": {oldCloudBaseName}<", file.Text);
            });
    }

    [Fact]
    public void ApplicationUploadChannels_ShouldNotKeepObsoleteUploaderBaseLayers()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var sourceDirectories = new[]
        {
            Path.Combine(repoRoot, "src", "Application"),
            Path.Combine(repoRoot, "src", "Modules"),
            Path.Combine(repoRoot, "src", "Runtime")
        };
        var obsoleteNames = new[]
        {
            "Process" + "CloudUploaderBase",
            "Process" + "MesUploaderBase",
            "Mes" + "UploadChannelBase"
        };

        var offendingFiles = sourceDirectories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repoRoot, path),
                Text = File.ReadAllText(path)
            })
            .Where(file => obsoleteNames.Any(name => file.Text.Contains(name, StringComparison.Ordinal)))
            .Select(file => file.Path)
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void MigrationWorkspace_ShouldNotContainLegacyUiSharedProject()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();

        var legacyUiProjectName = "IIoT.Edge.UI." + "Shared";
        Assert.False(Directory.Exists(Path.Combine(repoRoot, "src", "Shared", legacyUiProjectName)));
    }

    [Fact]
    public void PluginHardwareAndSampleRegistration_ShouldUseModuleBuilder()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var moduleFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src", "Modules"), "DependencyInjection.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repoRoot, path),
                Text = File.ReadAllText(path)
            })
            .ToArray();

        var forbiddenPatterns = new[]
        {
            "AddSingleton<IModulePlcSignalProfile",
            "AddSingleton<IModuleHardwareProfileProvider",
            "AddSingleton<IDevelopmentSampleContributor"
        };
        var offenders = moduleFiles
            .Where(file => forbiddenPatterns.Any(pattern => file.Text.Contains(pattern, StringComparison.Ordinal)))
            .Select(file => file.Path)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Runtime_ShouldNotKeepOldIoScanContractName()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var oldInterfaceName = "I" + "Signal" + "Interaction";
        var oldClassName = "Signal" + "Interaction";
        var offenders = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repoRoot, path),
                Text = File.ReadAllText(path)
            })
            .Where(file => file.Text.Contains(oldInterfaceName, StringComparison.Ordinal)
                           || file.Text.Contains(oldClassName, StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void PluginRuntime_ShouldNotUseStaticPlcProfileOrStringSignalAccessor()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var runtimeFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src", "Modules"), "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Runtime{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repoRoot, path),
                Text = File.ReadAllText(path)
            })
            .ToArray();

        var offenders = runtimeFiles
            .Where(file => file.Text.Contains("PlcSignalProfile.", StringComparison.Ordinal)
                           || Regex.IsMatch(file.Text, @"ILogicalSignalAccessor\s+\w"))
            .Select(file => file.Path)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void StartupDiagnostics_ShouldNotUseStaticBusinessCollaborators()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var bootstrapRoot = Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.Host.Bootstrap");
        var offenders = Directory
            .EnumerateFiles(bootstrapRoot, "StartupDiagnostics*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = Path.GetRelativePath(repoRoot, path),
                Text = File.ReadAllText(path)
            })
            .Where(file => file.Text.Contains("static class", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void HomogenizationWireDtosAndPayloadModels_ShouldStayInSeparatedFolders()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var moduleRoot = Path.Combine(repoRoot, "src", "Modules", "IIoT.Edge.Module.Homogenization");
        var payloadRoot = Path.Combine(moduleRoot, "Payload");

        Assert.False(
            File.Exists(Path.Combine(moduleRoot, "Integration", "Cloud", "HomogenizationCloudPayloadDtos.cs")),
            "Cloud wire DTOs must live under Integration/Dtos.");
        Assert.True(File.Exists(Path.Combine(moduleRoot, "Integration", "Dtos", "HomogenizationCloudPayloadDtos.cs")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot, "Integration", "Dtos", "Cloud")));
        Assert.False(Directory.Exists(Path.Combine(moduleRoot, "Integration", "Dtos", "Mes")));
        Assert.False(Directory.Exists(Path.Combine(payloadRoot, "Entities")));
        Assert.False(Directory.Exists(Path.Combine(payloadRoot, "Snapshots")));
        Assert.False(Directory.Exists(Path.Combine(payloadRoot, "Validation")));
        Assert.True(File.Exists(Path.Combine(payloadRoot, "HomogenizationCellData.cs")));
        Assert.True(File.Exists(Path.Combine(payloadRoot, "HomogenizationCellDataValidator.cs")));
        Assert.True(File.Exists(Path.Combine(payloadRoot, "HomogenizationEquipmentStatusSnapshot.cs")));
        Assert.True(File.Exists(Path.Combine(payloadRoot, "HomogenizationRealtimeSnapshot.cs")));
        Assert.True(File.Exists(Path.Combine(payloadRoot, "HomogenizationRecipeSnapshot.cs")));

        var payloadDtoReferences = Directory
            .EnumerateFiles(payloadRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = Path.GetRelativePath(repoRoot, path),
                Text = File.ReadAllText(path)
            })
            .Where(file => file.Text.Contains("Integration.Dtos", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToArray();

        Assert.Empty(payloadDtoReferences);
    }

    [Fact]
    public void HomogenizationConfiguration_ShouldUseWpfStyleSingleConfigurationFile()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var configRoot = Path.Combine(repoRoot, "src", "Modules", "IIoT.Edge.Module.Homogenization", "Config");

        Assert.True(File.Exists(Path.Combine(configRoot, "HomogenizationModuleConfiguration.cs")));
        Assert.True(File.Exists(Path.Combine(configRoot, "homogenization.module.json")));
        Assert.True(File.Exists(Path.Combine(configRoot, "README.md")));
        Assert.True(Directory.Exists(Path.Combine(configRoot, "Hardware")));
        Assert.True(Directory.Exists(Path.Combine(configRoot, "Parameters")));
        Assert.False(File.Exists(Path.Combine(configRoot, "HomogenizationMesOptions.cs")));
        Assert.False(File.Exists(Path.Combine(configRoot, "HomogenizationCodeOptions.cs")));
        Assert.False(File.Exists(Path.Combine(configRoot, "HomogenizationOptionsValidators.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Modules", "IIoT.Edge.Module.Homogenization", "HomogenizationModuleBase.cs")));
    }
}
