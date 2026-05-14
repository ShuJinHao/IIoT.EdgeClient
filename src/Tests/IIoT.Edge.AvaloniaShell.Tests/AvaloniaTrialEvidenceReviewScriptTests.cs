using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class AvaloniaTrialEvidenceReviewScriptTests
{
    [Fact]
    public void Review_script_accepts_minimal_evidence_package_and_marks_p1_pending()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "ReviewAvaloniaTrialEvidence.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-trial-review-" + Guid.NewGuid().ToString("N"));
        var evidenceRoot = Path.Combine(tempRoot, "evidence");
        var outputRoot = Path.Combine(tempRoot, "review");
        CreateMinimalEvidencePackage(evidenceRoot, includeReleaseManifest: true);

        var result = RunPowerShell(scriptPath, "-EvidencePath", evidenceRoot, "-OutputRoot", outputRoot);

        Assert.Equal(0, result.ExitCode);
        var summaryPath = Directory.GetFiles(outputRoot, "trial-review-summary.json", SearchOption.AllDirectories).Single();
        using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath, Encoding.UTF8));
        Assert.Equal("P1Pending", summary.RootElement.GetProperty("overallStatus").GetString());
        Assert.True(summary.RootElement.GetProperty("wpfFallbackVerified").GetBoolean());
        Assert.False(summary.RootElement.GetProperty("readyForDefaultEntryReview").GetBoolean());
        Assert.Equal(0, summary.RootElement.GetProperty("p0Blockers").GetArrayLength());
        Assert.True(summary.RootElement.GetProperty("p1Pending").GetArrayLength() > 0);
        Assert.False(summary.RootElement
            .GetProperty("p1Evidence")
            .GetProperty("completedAcceptanceRecord")
            .GetProperty("completed")
            .GetBoolean());
        Assert.All(summary.RootElement
            .GetProperty("p1Evidence")
            .GetProperty("requiredScreenshots")
            .EnumerateArray(), item => Assert.False(item.GetProperty("exists").GetBoolean()));

        var reportPath = Directory.GetFiles(outputRoot, "trial-review-report.md", SearchOption.AllDirectories).Single();
        Assert.Contains("Avalonia 试运行证据审查报告", File.ReadAllText(reportPath, Encoding.UTF8), StringComparison.Ordinal);
    }

    [Fact]
    public void Review_script_accepts_complete_evidence_package_and_marks_ready_for_default_entry_review()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "ReviewAvaloniaTrialEvidence.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-trial-review-" + Guid.NewGuid().ToString("N"));
        var evidenceRoot = Path.Combine(tempRoot, "evidence");
        var outputRoot = Path.Combine(tempRoot, "review");
        CreateMinimalEvidencePackage(
            evidenceRoot,
            includeReleaseManifest: true,
            includeCompletedAcceptance: true,
            includeRequiredScreenshots: true);

        var result = RunPowerShell(
            scriptPath,
            "-EvidencePath",
            evidenceRoot,
            "-OutputRoot",
            outputRoot,
            "-RequireCompletedAcceptance",
            "-RequireScreenshots");

        Assert.Equal(0, result.ExitCode);
        var summaryPath = Directory.GetFiles(outputRoot, "trial-review-summary.json", SearchOption.AllDirectories).Single();
        using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath, Encoding.UTF8));
        Assert.Equal("ReadyForDefaultEntryReview", summary.RootElement.GetProperty("overallStatus").GetString());
        Assert.True(summary.RootElement.GetProperty("readyForDefaultEntryReview").GetBoolean());
        Assert.Equal(0, summary.RootElement.GetProperty("p0Blockers").GetArrayLength());
        Assert.Equal(0, summary.RootElement.GetProperty("p1Pending").GetArrayLength());
        Assert.True(summary.RootElement
            .GetProperty("p1Evidence")
            .GetProperty("allRequiredScreenshotsPresent")
            .GetBoolean());
    }

    [Fact]
    public void Review_script_can_require_completed_acceptance_record()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "ReviewAvaloniaTrialEvidence.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-trial-review-" + Guid.NewGuid().ToString("N"));
        var evidenceRoot = Path.Combine(tempRoot, "evidence");
        var outputRoot = Path.Combine(tempRoot, "review");
        CreateMinimalEvidencePackage(evidenceRoot, includeReleaseManifest: true);

        var result = RunPowerShell(
            scriptPath,
            "-EvidencePath",
            evidenceRoot,
            "-OutputRoot",
            outputRoot,
            "-RequireCompletedAcceptance");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("验收记录", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_script_can_require_key_screenshots()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "ReviewAvaloniaTrialEvidence.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-trial-review-" + Guid.NewGuid().ToString("N"));
        var evidenceRoot = Path.Combine(tempRoot, "evidence");
        var outputRoot = Path.Combine(tempRoot, "review");
        CreateMinimalEvidencePackage(
            evidenceRoot,
            includeReleaseManifest: true,
            includeCompletedAcceptance: true);

        var result = RunPowerShell(
            scriptPath,
            "-EvidencePath",
            evidenceRoot,
            "-OutputRoot",
            outputRoot,
            "-RequireScreenshots");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("关键截图", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_script_fails_when_release_manifest_is_missing()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "ReviewAvaloniaTrialEvidence.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-trial-review-" + Guid.NewGuid().ToString("N"));
        var evidenceRoot = Path.Combine(tempRoot, "evidence");
        var outputRoot = Path.Combine(tempRoot, "review");
        CreateMinimalEvidencePackage(evidenceRoot, includeReleaseManifest: false);

        var result = RunPowerShell(scriptPath, "-EvidencePath", evidenceRoot, "-OutputRoot", outputRoot);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("release-manifest.json", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Decision_package_script_generates_review_materials_without_approving_default_entry()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "NewAvaloniaDefaultEntryDecisionPackage.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-decision-" + Guid.NewGuid().ToString("N"));
        var candidateSummaryPath = Path.Combine(tempRoot, "candidate-validation-summary.json");
        var trialReviewSummaryPath = Path.Combine(tempRoot, "trial-review-summary.json");
        var issueRecoveryPath = Path.Combine(tempRoot, "issues.md");
        var outputRoot = Path.Combine(tempRoot, "decision");
        Directory.CreateDirectory(tempRoot);
        WriteUtf8(
            candidateSummaryPath,
            """
            {
              "fullGate": true,
              "wpfFallback": { "verified": true },
              "previewPackages": [
                { "name": "SkiaSharp", "version": "3.119.4-preview.1.1" }
              ]
            }
            """);
        WriteUtf8(
            trialReviewSummaryPath,
            """
            {
              "overallStatus": "ReadyForDefaultEntryReview",
              "readyForDefaultEntryReview": true,
              "p0Blockers": [],
              "p1Pending": []
            }
            """);
        WriteUtf8(issueRecoveryPath, "# 问题回收清单");

        var result = RunPowerShell(
            scriptPath,
            "-CandidateSummaryPath",
            candidateSummaryPath,
            "-TrialReviewSummaryPath",
            trialReviewSummaryPath,
            "-IssueRecoveryPath",
            issueRecoveryPath,
            "-OutputRoot",
            outputRoot);

        Assert.Equal(0, result.ExitCode);
        var jsonPath = Directory.GetFiles(outputRoot, "default-entry-decision-package.json", SearchOption.AllDirectories).Single();
        var markdownPath = Directory.GetFiles(outputRoot, "default-entry-decision-package.md", SearchOption.AllDirectories).Single();
        using var package = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
        Assert.True(package.RootElement.GetProperty("readyForDefaultEntryReview").GetBoolean());
        Assert.Equal(JsonValueKind.Null, package.RootElement.GetProperty("finalDecision").GetProperty("allowedToSwitchDefaultEntry").ValueKind);
        var markdown = File.ReadAllText(markdownPath, Encoding.UTF8);
        Assert.Contains("可进入切默认入口评审", markdown, StringComparison.Ordinal);
        Assert.Contains("最终决策留空", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Decision_package_script_blocks_review_materials_when_trial_review_is_p1_pending()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "NewAvaloniaDefaultEntryDecisionPackage.ps1");
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-decision-" + Guid.NewGuid().ToString("N"));
        var candidateSummaryPath = Path.Combine(tempRoot, "candidate-validation-summary.json");
        var trialReviewSummaryPath = Path.Combine(tempRoot, "trial-review-summary.json");
        var issueRecoveryPath = Path.Combine(tempRoot, "issues.md");
        var outputRoot = Path.Combine(tempRoot, "decision");
        Directory.CreateDirectory(tempRoot);
        WriteUtf8(
            candidateSummaryPath,
            """
            {
              "fullGate": true,
              "wpfFallback": { "verified": true }
            }
            """);
        WriteUtf8(
            trialReviewSummaryPath,
            """
            {
              "overallStatus": "P1Pending",
              "readyForDefaultEntryReview": false,
              "p0Blockers": [],
              "p1Pending": [ "缺少关键截图" ]
            }
            """);
        WriteUtf8(issueRecoveryPath, "# 问题回收清单");

        var result = RunPowerShell(
            scriptPath,
            "-CandidateSummaryPath",
            candidateSummaryPath,
            "-TrialReviewSummaryPath",
            trialReviewSummaryPath,
            "-IssueRecoveryPath",
            issueRecoveryPath,
            "-OutputRoot",
            outputRoot);

        Assert.Equal(0, result.ExitCode);
        var markdownPath = Directory.GetFiles(outputRoot, "default-entry-decision-package.md", SearchOption.AllDirectories).Single();
        var markdown = File.ReadAllText(markdownPath, Encoding.UTF8);
        Assert.Contains("不允许进入切默认入口评审", markdown, StringComparison.Ordinal);
        Assert.Contains("最终决策留空", markdown, StringComparison.Ordinal);
    }

    private static void CreateMinimalEvidencePackage(
        string evidenceRoot,
        bool includeReleaseManifest,
        bool includeCompletedAcceptance = false,
        bool includeRequiredScreenshots = false)
    {
        Directory.CreateDirectory(evidenceRoot);
        Directory.CreateDirectory(Path.Combine(evidenceRoot, "launcher"));
        Directory.CreateDirectory(Path.Combine(evidenceRoot, "docs"));
        Directory.CreateDirectory(Path.Combine(evidenceRoot, "screenshots"));

        if (includeReleaseManifest)
        {
            WriteUtf8(
                Path.Combine(evidenceRoot, "release-manifest.json"),
                """
                {
                  "releaseKind": "AvaloniaMigration",
                  "commit": { "sha": "test" }
                }
                """);
        }

        WriteUtf8(
            Path.Combine(evidenceRoot, "candidate-validation-summary.json"),
            """
            {
              "wpfFallback": { "verified": true },
              "previewPackages": [
                { "name": "SkiaSharp", "version": "3.119.4-preview.1.1" }
              ]
            }
            """);
        WriteUtf8(Path.Combine(evidenceRoot, "field-evidence-summary.json"), "{}");
        WriteUtf8(Path.Combine(evidenceRoot, "diagnostics-summary.md"), "# Diagnostics");
        WriteUtf8(
            Path.Combine(evidenceRoot, "launcher", "launcher.profiles.json"),
            """
            [
              {
                "ProfileId": "HomogenizationLineAvalonia",
                "ExecutablePath": "..\\avalonia-shell\\IIoT.Edge.AvaloniaShell.exe"
              },
              {
                "ProfileId": "HomogenizationLineAvaloniaRuntime",
                "ExecutablePath": "..\\avalonia-shell\\IIoT.Edge.AvaloniaShell.exe",
                "Arguments": [ "--start-runtime" ]
              }
            ]
            """);
        WriteUtf8(Path.Combine(evidenceRoot, "docs", "Avalonia12-现场试运行验收记录模板.md"), "# 验收记录模板");
        WriteUtf8(Path.Combine(evidenceRoot, "screenshots", "截图占位说明.md"), "# 截图占位说明");

        if (includeCompletedAcceptance)
        {
            WriteUtf8(
                Path.Combine(evidenceRoot, "docs", "Avalonia12-现场试运行验收记录.md"),
                """
                # Avalonia 12 现场试运行验收记录

                验收记录状态：已完成

                | 项目 | 内容 |
                | --- | --- |
                | 试运行日期 | 2026-05-14 |
                | 产线/工位 | Line-A |
                | 操作人 | Tester |
                | 是否允许进入切默认入口评审 | 是 |
                """);
        }

        if (includeRequiredScreenshots)
        {
            WriteUtf8(Path.Combine(evidenceRoot, "screenshots", "01-diagnostics-summary.png"), "fake png");
            WriteUtf8(Path.Combine(evidenceRoot, "screenshots", "02-io-write-gate.png"), "fake png");
            WriteUtf8(Path.Combine(evidenceRoot, "screenshots", "03-plc-write-trace.png"), "fake png");
            WriteUtf8(Path.Combine(evidenceRoot, "screenshots", "04-wpf-fallback.png"), "fake png");
        }
    }

    private static (int ExitCode, string Output) RunPowerShell(string scriptPath, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = "powershell";
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static void WriteUtf8(string path, string content)
        => File.WriteAllText(path, content, Encoding.UTF8);

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
