using System.Text.RegularExpressions;

namespace IIoT.Edge.Module.ContractTests;

public sealed class PluginCommentContractTests
{
    private static readonly string[] BusinessDirectories =
    [
        "Payload",
        "Integration",
        "Runtime",
        "Config",
        "Constants",
        "Samples"
    ];

    private static readonly Regex TypeDeclarationRegex = new(
        @"^\s*(public|internal)\s+(sealed\s+|static\s+|abstract\s+|partial\s+)*(class|record|interface|enum)\s+(?<name>[A-Za-z0-9_]+)",
        RegexOptions.Compiled);

    private static readonly Regex PropertyDeclarationRegex = new(
        @"^\s*(public|internal|protected)\s+(override\s+|virtual\s+|static\s+|readonly\s+)*(?:[A-Za-z0-9_\.<>?\[\],]+\s+)+(?<name>[A-Za-z0-9_]+)\s*(\{|=>)",
        RegexOptions.Compiled);

    [Fact]
    public void PluginBusinessSource_ShouldDocumentBusinessTypesAndPropertiesWithChineseSummary()
    {
        var repoRoot = ContractTestPathHelper.FindRepoRoot();
        var moduleRoot = Path.Combine(repoRoot, "src", "Modules");
        var pluginFiles = Directory
            .EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsBusinessSourceFile)
            .ToArray();

        var missingSummaries = new List<string>();
        foreach (var file in pluginFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var declaration = TypeDeclarationRegex.Match(lines[index]);
                if (!declaration.Success)
                {
                    declaration = PropertyDeclarationRegex.Match(lines[index]);
                }

                if (!declaration.Success || HasChineseSummary(lines, index))
                {
                    continue;
                }

                missingSummaries.Add(
                    $"{Path.GetRelativePath(repoRoot, file)}:{index + 1} {declaration.Groups["name"].Value}");
            }
        }

        Assert.Empty(missingSummaries);
    }

    private static bool IsBusinessSourceFile(string file)
    {
        if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!file.Contains($"{Path.DirectorySeparatorChar}IIoT.Edge.Module.", StringComparison.Ordinal))
        {
            return false;
        }

        return BusinessDirectories.Any(directory =>
            file.Contains($"{Path.DirectorySeparatorChar}{directory}{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || file.EndsWith($"{Path.DirectorySeparatorChar}{directory}.cs", StringComparison.Ordinal));
    }

    private static bool HasChineseSummary(IReadOnlyList<string> lines, int declarationLine)
    {
        var start = Math.Max(0, declarationLine - 5);
        var text = string.Join('\n', lines.Skip(start).Take(declarationLine - start));
        return text.Contains("<summary>", StringComparison.Ordinal)
               && text.Contains("</summary>", StringComparison.Ordinal)
               && Regex.IsMatch(text, @"[\u4e00-\u9fff]");
    }
}
