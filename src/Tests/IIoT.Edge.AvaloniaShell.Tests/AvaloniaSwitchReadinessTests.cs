using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class AvaloniaSwitchReadinessTests
{
    [Fact]
    public void Switch_readiness_documents_define_matrix_and_blockers()
    {
        var root = FindRepositoryRoot();
        var matrix = File.ReadAllText(Path.Combine(root, "docs", "Avalonia12-切换前差异矩阵.md"));
        var blockers = File.ReadAllText(Path.Combine(root, "docs", "Avalonia12-切换阻断清单.md"));

        foreach (var feature in new[]
        {
            "Launcher",
            "Shell 主壳",
            "Header/Footer",
            "菜单/导航",
            "Dock 布局",
            "登录",
            "Monitor",
            "DataView",
            "Capacity",
            "PlcTaskBinding",
            "Diagnostics",
            "HardwareConfig",
            "IOView",
            "Recipe",
            "Param",
            "匀浆插件 UI"
        })
        {
            Assert.Contains(feature, matrix, StringComparison.Ordinal);
        }

        Assert.Contains("P0", blockers, StringComparison.Ordinal);
        Assert.Contains("P1", blockers, StringComparison.Ordinal);
        Assert.Contains("P2", blockers, StringComparison.Ordinal);
        Assert.Contains("Cloud/MES 清理、重试、删除", blockers, StringComparison.Ordinal);
        Assert.Contains("ReadDataAsync", blockers, StringComparison.Ordinal);
        Assert.Contains("WriteDataAsync", blockers, StringComparison.Ordinal);
    }

    [Fact]
    public void Candidate_validation_script_chains_publish_evidence_dependency_and_boundary_checks()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "TestAvaloniaMigrationCandidate.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("PublishAvaloniaMigration.ps1", script, StringComparison.Ordinal);
        Assert.Contains("CollectAvaloniaFieldEvidence.ps1", script, StringComparison.Ordinal);
        Assert.Contains("--include-transitive", script, StringComparison.Ordinal);
        Assert.Contains("--vulnerable", script, StringComparison.Ordinal);
        Assert.Contains("SkiaSharp.NativeAssets.Win32", script, StringComparison.Ordinal);
        Assert.Contains("System\\.Windows|UseWPF|IIoT\\.Edge\\.UI\\.Shared|SukiUI", script, StringComparison.Ordinal);
        Assert.Contains("ReadDataAsync|WriteDataAsync", script, StringComparison.Ordinal);
        Assert.Contains("candidate-validation-summary.json", script, StringComparison.Ordinal);
        Assert.DoesNotContain("IIoT.Edge.Shell.csproj', 'publish", script, StringComparison.Ordinal);
        Assert.DoesNotContain("src\\Edge\\IIoT.Edge.Launcher\\IIoT.Edge.Launcher.csproj", script, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}

