using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class AvaloniaReviewSnapshotWorkflowTests
{
    [Fact]
    public void Review_snapshot_sync_script_keeps_branch_model_and_generated_outputs_out()
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "SyncAvaloniaReviewSnapshot.ps1");

        Assert.True(File.Exists(scriptPath), $"Missing script: {scriptPath}");

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("codex/avalonia-default-entry-review", script, StringComparison.Ordinal);
        Assert.Contains("codex/avalonia-default-entry-review-pr", script, StringComparison.Ordinal);
        Assert.Contains("git@github.com:ShuJinHao/IIoT.EdgeClient.git", script, StringComparison.Ordinal);
        Assert.Contains("'diff', '--check", script, StringComparison.Ordinal);
        Assert.Contains("diff', '--cached', '--check", script, StringComparison.Ordinal);
        Assert.Contains("robocopy", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/MIR", script, StringComparison.Ordinal);
        Assert.Contains("previousErrorActionPreference", script, StringComparison.Ordinal);
        Assert.Contains(".artifacts", script, StringComparison.Ordinal);
        Assert.Contains("TestResults", script, StringComparison.Ordinal);
        Assert.Contains("node_modules", script, StringComparison.Ordinal);
        Assert.Contains("Use -Commit together with -Push", script, StringComparison.Ordinal);
        Assert.Contains("--force-with-lease", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadDataAsync", script, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteDataAsync", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Branch_sync_document_records_long_lived_branch_and_review_branch_rules()
    {
        var repoRoot = FindRepositoryRoot();
        var documentPath = Path.Combine(repoRoot, "docs", "Avalonia12-长期分支与审核同步规范.md");

        Assert.True(File.Exists(documentPath), $"Missing document: {documentPath}");

        var text = File.ReadAllText(documentPath);
        Assert.Contains("codex/avalonia-default-entry-review", text, StringComparison.Ordinal);
        Assert.Contains("codex/avalonia-default-entry-review-pr", text, StringComparison.Ordinal);
        Assert.Contains("PR #44", text, StringComparison.Ordinal);
        Assert.Contains("git diff --check", text, StringComparison.Ordinal);
        Assert.Contains("--force-with-lease", text, StringComparison.Ordinal);
        Assert.Contains("TestResults", text, StringComparison.Ordinal);
        Assert.Contains("ApprovedForDefaultEntrySwitch", text, StringComparison.Ordinal);
        Assert.Contains("WPF Launcher/WPF Shell", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Claude_github_review_checklist_covers_boundaries_and_gate_conditions()
    {
        var repoRoot = FindRepositoryRoot();
        var documentPath = Path.Combine(repoRoot, "docs", "Avalonia12-Claude-GitHub审核清单.md");

        Assert.True(File.Exists(documentPath), $"Missing document: {documentPath}");

        var text = File.ReadAllText(documentPath);
        Assert.Contains("System.Windows", text, StringComparison.Ordinal);
        Assert.Contains("IIoT.Edge.UI.Shared", text, StringComparison.Ordinal);
        Assert.Contains("SukiUI", text, StringComparison.Ordinal);
        Assert.Contains("ReadDataAsync", text, StringComparison.Ordinal);
        Assert.Contains("WriteDataAsync", text, StringComparison.Ordinal);
        Assert.Contains("Cloud/MES", text, StringComparison.Ordinal);
        Assert.Contains("P1Pending", text, StringComparison.Ordinal);
        Assert.Contains("SkiaSharp", text, StringComparison.Ordinal);
        Assert.Contains("NU190x", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Twentieth_batch_record_keeps_review_state_and_p1_pending_explicit()
    {
        var repoRoot = FindRepositoryRoot();
        var recordPath = Path.Combine(repoRoot, "docs", "Avalonia12-第二十批迁移记录.md");

        Assert.True(File.Exists(recordPath), $"Missing record: {recordPath}");

        var text = File.ReadAllText(recordPath);
        Assert.Contains("https://github.com/ShuJinHao/IIoT.EdgeClient/pull/44", text, StringComparison.Ordinal);
        Assert.Contains("codex/avalonia-default-entry-review", text, StringComparison.Ordinal);
        Assert.Contains("codex/avalonia-default-entry-review-pr", text, StringComparison.Ordinal);
        Assert.Contains("Draft", text, StringComparison.Ordinal);
        Assert.Contains("P1Pending", text, StringComparison.Ordinal);
        Assert.Contains("FullGate", text, StringComparison.Ordinal);
        Assert.Contains("默认入口真实切换仍需后续独立批次", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Pr44_review_feedback_record_keeps_no_feedback_distinct_from_approval()
    {
        var repoRoot = FindRepositoryRoot();
        var recordPath = Path.Combine(repoRoot, "docs", "Avalonia12-PR44审核反馈回收记录.md");

        Assert.True(File.Exists(recordPath), $"Missing record: {recordPath}");

        var text = File.ReadAllText(recordPath);
        Assert.Contains("https://github.com/ShuJinHao/IIoT.EdgeClient/pull/44", text, StringComparison.Ordinal);
        Assert.Contains("暂无 review 评论", text, StringComparison.Ordinal);
        Assert.Contains("无评论", text, StringComparison.Ordinal);
        Assert.Contains("审核通过", text, StringComparison.Ordinal);
        Assert.Contains("Cloud/MES", text, StringComparison.Ordinal);
        Assert.Contains("ReadDataAsync", text, StringComparison.Ordinal);
        Assert.Contains("WriteDataAsync", text, StringComparison.Ordinal);
        Assert.Contains("需用户确认", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Field_evidence_minimal_sample_keeps_p1_pending_gate_explicit()
    {
        var repoRoot = FindRepositoryRoot();
        var documentPath = Path.Combine(repoRoot, "docs", "Avalonia12-现场证据包预审最小样例说明.md");

        Assert.True(File.Exists(documentPath), $"Missing document: {documentPath}");

        var text = File.ReadAllText(documentPath);
        Assert.Contains("release-manifest.json", text, StringComparison.Ordinal);
        Assert.Contains("candidate-validation-summary.json", text, StringComparison.Ordinal);
        Assert.Contains("launcher.profiles.json", text, StringComparison.Ordinal);
        Assert.Contains("01-diagnostics-summary.png", text, StringComparison.Ordinal);
        Assert.Contains("02-io-write-gate.png", text, StringComparison.Ordinal);
        Assert.Contains("03-plc-write-trace.png", text, StringComparison.Ordinal);
        Assert.Contains("04-wpf-fallback.png", text, StringComparison.Ordinal);
        Assert.Contains("P0Blocked", text, StringComparison.Ordinal);
        Assert.Contains("P1Pending", text, StringComparison.Ordinal);
        Assert.Contains("ReadyForDefaultEntryReview", text, StringComparison.Ordinal);
        Assert.Contains("ApprovedForDefaultEntrySwitch", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Twenty_first_batch_record_keeps_review_and_field_evidence_scope()
    {
        var repoRoot = FindRepositoryRoot();
        var recordPath = Path.Combine(repoRoot, "docs", "Avalonia12-第二十一批迁移记录.md");

        Assert.True(File.Exists(recordPath), $"Missing record: {recordPath}");

        var text = File.ReadAllText(recordPath);
        Assert.Contains("PR #44", text, StringComparison.Ordinal);
        Assert.Contains("Draft", text, StringComparison.Ordinal);
        Assert.Contains("P1Pending", text, StringComparison.Ordinal);
        Assert.Contains("Cloud/MES", text, StringComparison.Ordinal);
        Assert.Contains("默认入口真实切换仍需后续独立批次", text, StringComparison.Ordinal);
        Assert.Contains("FullGate", text, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Cannot locate IIoT.EdgeClient.AvaloniaMigration repository root.");
    }
}
