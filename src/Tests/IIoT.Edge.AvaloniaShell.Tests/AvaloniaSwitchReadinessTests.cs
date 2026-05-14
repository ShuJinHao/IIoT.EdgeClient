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

        var trialManual = File.ReadAllText(Path.Combine(root, "docs", "Avalonia12-现场试运行手册.md"));
        var acceptanceTemplate = File.ReadAllText(Path.Combine(root, "docs", "Avalonia12-现场试运行验收记录模板.md"));
        var issueRecovery = File.ReadAllText(Path.Combine(root, "docs", "Avalonia12-试运行问题回收清单.md"));
        var decisionTemplate = File.ReadAllText(Path.Combine(root, "docs", "Avalonia12-切默认入口决策包模板.md"));
        var evidenceGuide = File.ReadAllText(Path.Combine(root, "docs", "Avalonia12-现场证据回收操作说明.md"));
        var importGuide = File.ReadAllText(Path.Combine(root, "docs", "Avalonia12-现场证据导入预审说明.md"));
        Assert.Contains("UI-only", trialManual, StringComparison.Ordinal);
        Assert.Contains("--start-runtime", trialManual, StringComparison.Ordinal);
        Assert.Contains("回退 WPF", trialManual, StringComparison.Ordinal);
        Assert.Contains("P0 阻断项", acceptanceTemplate, StringComparison.Ordinal);
        Assert.Contains("WPF Shell 回退", acceptanceTemplate, StringComparison.Ordinal);
        Assert.Contains("P1 问题", issueRecovery, StringComparison.Ordinal);
        Assert.Contains("可由证据关闭", issueRecovery, StringComparison.Ordinal);
        Assert.Contains("ReadyForDefaultEntryReview", issueRecovery, StringComparison.Ordinal);
        Assert.Contains("是否允许进入切默认入口评审", decisionTemplate, StringComparison.Ordinal);
        Assert.Contains("回退负责人", decisionTemplate, StringComparison.Ordinal);
        Assert.Contains("01-diagnostics-summary.png", evidenceGuide, StringComparison.Ordinal);
        Assert.Contains("ReviewAvaloniaTrialEvidence.ps1", evidenceGuide, StringComparison.Ordinal);
        Assert.Contains("NewAvaloniaDefaultEntryDecisionPackage.ps1", evidenceGuide, StringComparison.Ordinal);
        Assert.Contains("ImportAvaloniaFieldEvidence.ps1", importGuide, StringComparison.Ordinal);
        Assert.Contains("证据不足时不得关闭 P1", importGuide, StringComparison.Ordinal);
        Assert.Contains("field-evidence-review-bundle", importGuide, StringComparison.Ordinal);

        var readinessGuide = File.ReadAllText(Path.Combine(root, "docs", "Avalonia12-默认入口切换评审说明.md"));
        var previewTemplate = File.ReadAllText(Path.Combine(root, "docs", "Avalonia12-默认入口切换预演报告模板.md"));
        Assert.Contains("TestAvaloniaDefaultEntryReadiness.ps1", readinessGuide, StringComparison.Ordinal);
        Assert.Contains("SwitchAvaloniaDefaultEntry.ps1", readinessGuide, StringComparison.Ordinal);
        Assert.Contains("RestoreAvaloniaDefaultEntry.ps1", readinessGuide, StringComparison.Ordinal);
        Assert.Contains("-Apply", readinessGuide, StringComparison.Ordinal);
        Assert.Contains("不修改 Launcher profile", readinessGuide, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.Launcher.exe", previewTemplate, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.Launcher.Avalonia.exe", previewTemplate, StringComparison.Ordinal);

        var applyGuide = File.ReadAllText(Path.Combine(root, "docs", "Avalonia12-默认入口真实切换与回退说明.md"));
        Assert.Contains("rollback-snapshot", applyGuide, StringComparison.Ordinal);
        Assert.Contains("RestoreAvaloniaDefaultEntry.ps1", applyGuide, StringComparison.Ordinal);
        Assert.Contains("不改源码", applyGuide, StringComparison.Ordinal);
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
        Assert.Contains("ImportAvaloniaFieldEvidence.ps1", script, StringComparison.Ordinal);
        Assert.Contains("--include-transitive", script, StringComparison.Ordinal);
        Assert.Contains("--vulnerable", script, StringComparison.Ordinal);
        Assert.Contains("SkiaSharp.NativeAssets.Win32", script, StringComparison.Ordinal);
        Assert.Contains("System\\.Windows|UseWPF|IIoT\\.Edge\\.UI\\.Shared|SukiUI", script, StringComparison.Ordinal);
        Assert.Contains("ReadDataAsync|WriteDataAsync", script, StringComparison.Ordinal);
        Assert.Contains("VerifyWpfFallback", script, StringComparison.Ordinal);
        Assert.Contains("FullGate", script, StringComparison.Ordinal);
        Assert.Contains("effectiveRegressionTests", script, StringComparison.Ordinal);
        Assert.Contains("testResults", script, StringComparison.Ordinal);
        Assert.Contains("ReviewAvaloniaTrialEvidence.ps1", script, StringComparison.Ordinal);
        Assert.Contains("RestoreAvaloniaDefaultEntry.ps1", script, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.Launcher.csproj", script, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.Shell.csproj", script, StringComparison.Ordinal);
        Assert.Contains("wpfFallback", script, StringComparison.Ordinal);
        Assert.Contains("candidate-validation-summary.json", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Trial_run_script_defaults_to_ui_only_and_requires_explicit_runtime_switch()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "StartAvaloniaTrialRun.ps1");
        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("[switch]$StartRuntime", script, StringComparison.Ordinal);
        Assert.Contains("$arguments += '--start-runtime'", script, StringComparison.Ordinal);
        Assert.Contains("运行联调必须直接启动 AvaloniaShell", script, StringComparison.Ordinal);
        Assert.Contains("trial-run-logs", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadDataAsync", script, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteDataAsync", script, StringComparison.Ordinal);
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
