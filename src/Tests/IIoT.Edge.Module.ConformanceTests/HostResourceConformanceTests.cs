namespace IIoT.Edge.Module.ConformanceTests;

public sealed class HostResourceConformanceTests
{
    [Fact]
    public void Header_ShouldNotReferenceRemovedCompanyLogoResource()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        const string logoFileName = "logo.png";
        var logoPath = Path.Combine(
            repoRoot,
            "src",
            "Shared",
            "IIoT.Edge.UI.Shared",
            "Assets",
            "images",
            logoFileName);
        var filesWithReferences = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src"), "*.*", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .Where(path => path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(logoFileName, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .ToArray();

        Assert.False(File.Exists(logoPath), $"公司标志资源应删除：{logoPath}");
        Assert.Empty(filesWithReferences);
    }

    private static bool IsBuildArtifact(string path)
        => path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
           || path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
