namespace IIoT.Edge.Module.ContractTests;

using System.Text.RegularExpressions;
using System.Xml.Linq;

public sealed class ArchitectureBoundaryContractTests
{
    private static readonly string[] ForbiddenModuleNamespaces =
    [
        "IIoT.Edge.Module.Homogenization"
    ];

    [Fact]
    public void HostAndCommonProjects_ShouldNotReferenceConcreteModuleNamespaces()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var directories = new[]
        {
            Path.Combine(repoRoot, "src", "Core"),
            Path.Combine(repoRoot, "src", "Application", "IIoT.Edge.Application"),
            Path.Combine(repoRoot, "src", "Infrastructure"),
            Path.Combine(repoRoot, "src", "Presentation"),
            Path.Combine(repoRoot, "src", "Shared", "IIoT.Edge.SharedKernel"),
            Path.Combine(repoRoot, "src", "Shared", "IIoT.Edge.UI.Shared"),
            Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.Shell"),
            Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.Host.Bootstrap"),
            Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.Host.DataPipeline")
        };

        var offendingFiles = directories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)))
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
    public void ConcreteModuleNamespaces_ShouldOnlyAppearInModulesAndTests()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var allowedRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Modules"),
            Path.Combine(repoRoot, "src", "Tests")
        };

        var offendingFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsBuildArtifact(path))
            .Where(path => !allowedRoots.Any(root => IsUnderDirectory(root, path)))
            .Select(path => new
            {
                Path = path,
                Text = File.ReadAllText(path)
            })
            .Where(file => ForbiddenModuleNamespaces.Any(namespaceName => file.Text.Contains(namespaceName, StringComparison.Ordinal)))
            .Select(file => ToRepositoryPath(repoRoot, file.Path))
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void ProcessModules_ShouldOnlyReferenceApprovedHostContracts()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var approvedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "src/Application/IIoT.Edge.Application/IIoT.Edge.Application.csproj",
            "src/Modules/IIoT.Edge.Module.Sdk/IIoT.Edge.Module.Sdk.csproj",
            "src/Shared/IIoT.Edge.SharedKernel/IIoT.Edge.SharedKernel.csproj",
            "src/Shared/IIoT.Edge.UI.Shared/IIoT.Edge.UI.Shared.csproj",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/IIoT.Edge.Presentation.Navigation.csproj"
        };

        var offendingReferences = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src", "Modules"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}IIoT.Edge.Module.Sdk{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(projectPath => ReadProjectReferences(repoRoot, projectPath)
                .Where(referencePath => !approvedReferences.Contains(referencePath))
                .Select(referencePath => $"{ToRepositoryPath(repoRoot, projectPath)} -> {referencePath}"))
            .ToArray();

        Assert.Empty(offendingReferences);
    }

    [Fact]
    public void ModuleSdk_ShouldNotReferenceDataPipelineRuntime()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var sdkProject = Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "IIoT.Edge.Module.Sdk",
            "IIoT.Edge.Module.Sdk.csproj");

        var offendingReferences = ReadProjectReferences(repoRoot, sdkProject)
            .Where(referencePath => referencePath.Contains("IIoT.Edge.Host.DataPipeline", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(offendingReferences);
    }

    [Fact]
    public void ProcessModules_ShouldNotReferenceDataPipelineRuntime()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var offendingReferences = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src", "Modules"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}IIoT.Edge.Module.Sdk{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(projectPath => ReadProjectReferences(repoRoot, projectPath)
                .Where(referencePath => referencePath.Contains("IIoT.Edge.Host.DataPipeline", StringComparison.OrdinalIgnoreCase))
                .Select(referencePath => $"{ToRepositoryPath(repoRoot, projectPath)} -> {referencePath}"))
            .ToArray();

        Assert.Empty(offendingReferences);
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
            Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.Host.DataPipeline")
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
    public void Header_ShouldNotReferenceCompanyLogoResource()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var logoFileName = "logo" + ".png";
        var logoPath = Path.Combine(repoRoot, "src", "Shared", "IIoT.Edge.UI.Shared", "Assets", "images", logoFileName);
        var filesWithReferences = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src"), "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(logoFileName, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .ToArray();

        Assert.False(File.Exists(logoPath), $"公司标志资源应删除：{logoPath}");
        Assert.Empty(filesWithReferences);
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

    private static string[] ReadProjectReferences(string repoRoot, string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        return XDocument
            .Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!.Replace('\\', Path.DirectorySeparatorChar))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include)))
            .Select(path => ToRepositoryPath(repoRoot, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsBuildArtifact(string path)
    {
        return path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderDirectory(string directory, string path)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relativePath)
               && !relativePath.Equals("..", StringComparison.Ordinal)
               && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string ToRepositoryPath(string repoRoot, string path)
    {
        return Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
    }
}
