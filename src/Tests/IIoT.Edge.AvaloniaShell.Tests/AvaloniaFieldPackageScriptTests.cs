using System.Text.Json;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class AvaloniaFieldPackageScriptTests
{
    [Fact]
    public void Avalonia_publish_script_declares_candidate_package_layout_and_manifest()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "PublishAvaloniaMigration.ps1");

        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("release-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.Launcher.Avalonia.csproj", script, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.AvaloniaShell.csproj", script, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.Module.Homogenization.Avalonia.dll", script, StringComparison.Ordinal);
        Assert.Contains("fieldChecklistRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("nugetExceptionRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("switchMatrixRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("switchBlockerRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("trialManualRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("trialAcceptanceTemplateRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("trialIssueRecoveryRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("defaultEntryDecisionTemplateRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("fieldEvidenceGuideRelativePath", script, StringComparison.Ordinal);
        Assert.Contains("StartAvaloniaTrialRun.ps1", script, StringComparison.Ordinal);
        Assert.Contains("ReviewAvaloniaTrialEvidence.ps1", script, StringComparison.Ordinal);
        Assert.Contains("NewAvaloniaDefaultEntryDecisionPackage.ps1", script, StringComparison.Ordinal);
        Assert.Contains("TestAvaloniaDefaultEntryReadiness.ps1", script, StringComparison.Ordinal);
        Assert.Contains("SwitchAvaloniaDefaultEntry.ps1", script, StringComparison.Ordinal);
        Assert.Contains("ImportAvaloniaFieldEvidence.ps1", script, StringComparison.Ordinal);
        Assert.Contains("Avalonia12-现场证据导入预审说明.md", script, StringComparison.Ordinal);
        Assert.Contains("Join-UnicodeName", script, StringComparison.Ordinal);
        Assert.Contains("SkiaSharp", script, StringComparison.Ordinal);
        Assert.DoesNotContain("IIoT.Edge.Shell.csproj", script, StringComparison.Ordinal);
        Assert.DoesNotContain("IIoT.Edge.Launcher\\IIoT.Edge.Launcher.csproj", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_evidence_script_is_read_only_and_collects_expected_materials()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "CollectAvaloniaFieldEvidence.ps1");

        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("field-evidence-summary.json", script, StringComparison.Ordinal);
        Assert.Contains("launcher.profiles.json", script, StringComparison.Ordinal);
        Assert.Contains("截图占位说明.md", script, StringComparison.Ordinal);
        Assert.Contains("Avalonia12-切换前差异矩阵.md", script, StringComparison.Ordinal);
        Assert.Contains("Avalonia12-切换阻断清单.md", script, StringComparison.Ordinal);
        Assert.Contains("Avalonia12-现场试运行手册.md", script, StringComparison.Ordinal);
        Assert.Contains("Avalonia12-现场试运行验收记录模板.md", script, StringComparison.Ordinal);
        Assert.Contains("Avalonia12-试运行问题回收清单.md", script, StringComparison.Ordinal);
        Assert.Contains("Avalonia12-切默认入口决策包模板.md", script, StringComparison.Ordinal);
        Assert.Contains("Avalonia12-现场证据回收操作说明.md", script, StringComparison.Ordinal);
        Assert.Contains("01-diagnostics-summary.png", script, StringComparison.Ordinal);
        Assert.Contains("02-io-write-gate.png", script, StringComparison.Ordinal);
        Assert.Contains("03-plc-write-trace.png", script, StringComparison.Ordinal);
        Assert.Contains("04-wpf-fallback.png", script, StringComparison.Ordinal);
        Assert.Contains("release-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("candidate-validation-summary.json", script, StringComparison.Ordinal);
        Assert.Contains("diagnostics", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet ef", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trial_evidence_review_script_is_read_only_and_reports_gate_state()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "ReviewAvaloniaTrialEvidence.ps1");

        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("trial-review-summary.json", script, StringComparison.Ordinal);
        Assert.Contains("trial-review-report.md", script, StringComparison.Ordinal);
        Assert.Contains("release-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("candidate-validation-summary.json", script, StringComparison.Ordinal);
        Assert.Contains("launcher.profiles.json", script, StringComparison.Ordinal);
        Assert.Contains("截图占位说明", script, StringComparison.Ordinal);
        Assert.Contains("P0Blocked", script, StringComparison.Ordinal);
        Assert.Contains("P1Pending", script, StringComparison.Ordinal);
        Assert.Contains("ReadyForDefaultEntryReview", script, StringComparison.Ordinal);
        Assert.Contains("RequireCompletedAcceptance", script, StringComparison.Ordinal);
        Assert.Contains("RequireScreenshots", script, StringComparison.Ordinal);
        Assert.Contains("p1Evidence", script, StringComparison.Ordinal);
        Assert.Contains("diagnostics-summary", script, StringComparison.Ordinal);
        Assert.Contains("io-write-gate", script, StringComparison.Ordinal);
        Assert.Contains("plc-write-trace", script, StringComparison.Ordinal);
        Assert.Contains("wpf-fallback", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadDataAsync", script, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteDataAsync", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_entry_decision_package_script_is_read_only_and_keeps_human_decision_blank()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "NewAvaloniaDefaultEntryDecisionPackage.ps1");

        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("default-entry-decision-package.json", script, StringComparison.Ordinal);
        Assert.Contains("default-entry-decision-package.md", script, StringComparison.Ordinal);
        Assert.Contains("ReadyForDefaultEntryReview", script, StringComparison.Ordinal);
        Assert.Contains("不允许进入切默认入口评审", script, StringComparison.Ordinal);
        Assert.Contains("等待人工签字", script, StringComparison.Ordinal);
        Assert.Contains("allowedToSwitchDefaultEntry = $null", script, StringComparison.Ordinal);
        Assert.Contains("rollbackOwner = $null", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadDataAsync", script, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteDataAsync", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_entry_readiness_and_switch_scripts_are_gate_only_and_dry_run()
    {
        var repoRoot = FindRepositoryRoot();
        var readinessScriptPath = Path.Combine(repoRoot, "scripts", "TestAvaloniaDefaultEntryReadiness.ps1");
        var switchScriptPath = Path.Combine(repoRoot, "scripts", "SwitchAvaloniaDefaultEntry.ps1");

        Assert.True(File.Exists(readinessScriptPath), $"Missing script: {readinessScriptPath}");
        Assert.True(File.Exists(switchScriptPath), $"Missing script: {switchScriptPath}");

        var readinessScript = File.ReadAllText(readinessScriptPath);
        Assert.Contains("ApprovedForDefaultEntrySwitch", readinessScript, StringComparison.Ordinal);
        Assert.Contains("DefaultEntrySwitchRejected", readinessScript, StringComparison.Ordinal);
        Assert.Contains("allowedToSwitchDefaultEntry", readinessScript, StringComparison.Ordinal);
        Assert.Contains("rollbackOwner", readinessScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", readinessScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", readinessScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadDataAsync", readinessScript, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteDataAsync", readinessScript, StringComparison.Ordinal);

        var switchScript = File.ReadAllText(switchScriptPath);
        Assert.Contains("default-entry-switch-preview.json", switchScript, StringComparison.Ordinal);
        Assert.Contains("ApprovedForDefaultEntrySwitch", switchScript, StringComparison.Ordinal);
        Assert.Contains("wouldModifyFiles = $false", switchScript, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.Launcher.exe", switchScript, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.Launcher.Avalonia.exe", switchScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", switchScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-Content -LiteralPath $launcherProfilesPath", switchScript, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadDataAsync", switchScript, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteDataAsync", switchScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Field_evidence_import_script_is_read_only_and_chains_review_precheck_bundle()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "ImportAvaloniaFieldEvidence.ps1");

        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("evidence-import-summary.json", script, StringComparison.Ordinal);
        Assert.Contains("evidence-file-inventory.json", script, StringComparison.Ordinal);
        Assert.Contains("field-evidence-review-bundle", script, StringComparison.Ordinal);
        Assert.Contains("ReviewAvaloniaTrialEvidence.ps1", script, StringComparison.Ordinal);
        Assert.Contains("NewAvaloniaDefaultEntryDecisionPackage.ps1", script, StringComparison.Ordinal);
        Assert.Contains("TestAvaloniaDefaultEntryReadiness.ps1", script, StringComparison.Ordinal);
        Assert.Contains("SwitchAvaloniaDefaultEntry.ps1", script, StringComparison.Ordinal);
        Assert.Contains("RequireCompletedAcceptance", script, StringComparison.Ordinal);
        Assert.Contains("RequireScreenshots", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadDataAsync", script, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteDataAsync", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Launcher_avalonia_profiles_keep_ui_only_default_and_runtime_entry()
    {
        var repoRoot = FindRepositoryRoot();
        var profilesPath = Path.Combine(
            repoRoot,
            "src",
            "Edge",
            "IIoT.Edge.Launcher.Avalonia",
            "launcher.profiles.json");

        using var document = JsonDocument.Parse(File.ReadAllText(profilesPath));
        var profiles = document.RootElement.EnumerateArray().ToArray();

        var uiOnly = profiles.Single(profile =>
            string.Equals(profile.GetProperty("ProfileId").GetString(), "HomogenizationLineAvalonia", StringComparison.Ordinal));
        Assert.Equal("..\\avalonia-shell\\IIoT.Edge.AvaloniaShell.exe", uiOnly.GetProperty("ExecutablePath").GetString());
        Assert.False(uiOnly.TryGetProperty("Arguments", out var uiOnlyArguments) &&
                     uiOnlyArguments.ValueKind == JsonValueKind.Array &&
                     uiOnlyArguments.GetArrayLength() > 0);

        var runtime = profiles.Single(profile =>
            string.Equals(profile.GetProperty("ProfileId").GetString(), "HomogenizationLineAvaloniaRuntime", StringComparison.Ordinal));
        Assert.Contains(runtime.GetProperty("Arguments").EnumerateArray(), item =>
            string.Equals(item.GetString(), "--start-runtime", StringComparison.Ordinal));
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
