namespace IIoT.Edge.Module.ContractTests;

using System.Text.RegularExpressions;

public sealed class ModuleDynamicConformanceTests
{
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
    public void PluginProductionTasks_ShouldHandleDataPipelineEnqueueExceptionsInsideTask()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var taskFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src", "Modules"), "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Production{Path.DirectorySeparatorChar}Tasks{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !IsBuildArtifact(path))
            .Select(path => new
            {
                Path = ToRepositoryPath(repoRoot, path),
                Text = File.ReadAllText(path)
            })
            .ToArray();

        var offenders = taskFiles
            .SelectMany(file => FindUnprotectedEnqueueCalls(file.Text)
                .Select(lineNumber => $"{file.Path}:{lineNumber}"))
            .ToArray();

        Assert.Empty(offenders);
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

    private static bool IsBuildArtifact(string path)
    {
        return path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToRepositoryPath(string repoRoot, string path)
    {
        return Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
    }

    private static IEnumerable<int> FindUnprotectedEnqueueCalls(string text)
    {
        var protectedRanges = Regex
            .Matches(
                text,
                @"\btry\s*\{.*?\}\s*catch\s*\(\s*Exception(?:\s+\w+)?\s*\)",
                RegexOptions.Singleline)
            .Select(match => (Start: match.Index, End: match.Index + match.Length))
            .ToArray();

        foreach (Match match in Regex.Matches(text, @"\.EnqueueAsync\s*\("))
        {
            if (protectedRanges.Any(range => match.Index >= range.Start && match.Index < range.End))
            {
                continue;
            }

            yield return GetLineNumber(text, match.Index);
        }
    }

    private static int GetLineNumber(string text, int index)
        => text.Take(index).Count(static character => character == '\n') + 1;
}
