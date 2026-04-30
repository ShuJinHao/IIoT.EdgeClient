namespace IIoT.Edge.Module.ContractTests;

public sealed class ArchitectureBoundaryContractTests
{
    private static readonly string[] ForbiddenModuleNamespaces =
    [
        "IIoT.Edge.Module.Injection",
        "IIoT.Edge.Module.Stacking",
        "IIoT.Edge.Module.Homogenization"
    ];

    [Fact]
    public void HostAndCommonProjects_ShouldNotReferenceConcreteModuleNamespaces()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var directories = new[]
        {
            Path.Combine(repoRoot, "src", "Application", "IIoT.Edge.Application"),
            Path.Combine(repoRoot, "src", "Runtime"),
            Path.Combine(repoRoot, "src", "Infrastructure", "IIoT.Edge.Infrastructure.Integration"),
            Path.Combine(repoRoot, "src", "Presentation"),
            Path.Combine(repoRoot, "src", "Shared", "IIoT.Edge.SharedKernel"),
            Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.Shell"),
            Path.Combine(repoRoot, "src", "Edge", "IIoT.Edge.Host.Bootstrap")
        };

        var offendingFiles = directories
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
            .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(logoFileName, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .ToArray();

        Assert.False(File.Exists(logoPath), $"公司标志资源应删除：{logoPath}");
        Assert.Empty(filesWithReferences);
    }
}
