namespace IIoT.Edge.Module.ContractTests;

public sealed class ArchitectureBoundaryContractTests
{
    private static readonly string[] ForbiddenModuleNamespaces =
    [
        "IIoT.Edge.Module.Injection",
        "IIoT.Edge.Module.Stacking",
        "IIoT.Edge.Module.DryRun"
    ];

    [Fact]
    public void HostAndCommonProjects_ShouldNotReferenceConcreteModuleNamespaces()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var directories = new[]
        {
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
}
