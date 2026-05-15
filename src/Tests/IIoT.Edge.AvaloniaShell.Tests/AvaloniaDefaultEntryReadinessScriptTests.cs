using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class AvaloniaDefaultEntryReadinessScriptTests
{
    [Fact]
    public void Readiness_script_rejects_p1_pending_decision_package()
    {
        var fixture = CreateReadinessFixture(
            trialStatus: "P1Pending",
            trialReady: false,
            p1Pending: ["缺少关键截图"],
            wpfFallbackVerified: true,
            fullGate: true,
            includeHumanApproval: true);

        var result = RunPowerShell(
            GetScriptPath("TestAvaloniaDefaultEntryReadiness.ps1"),
            "-DecisionPackagePath",
            fixture.DecisionPackagePath,
            "-OutputRoot",
            fixture.OutputRoot);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("默认入口切换评审门禁未通过", result.Output, StringComparison.Ordinal);
        var summary = ReadSingleSummary(fixture.OutputRoot, "default-entry-readiness-summary.json");
        Assert.Equal("DefaultEntrySwitchRejected", summary.RootElement.GetProperty("overallStatus").GetString());
    }

    [Fact]
    public void Readiness_script_rejects_ready_package_without_human_signature()
    {
        var fixture = CreateReadinessFixture(
            trialStatus: "ReadyForDefaultEntryReview",
            trialReady: true,
            p1Pending: [],
            wpfFallbackVerified: true,
            fullGate: true,
            includeHumanApproval: false);

        var result = RunPowerShell(
            GetScriptPath("TestAvaloniaDefaultEntryReadiness.ps1"),
            "-DecisionPackagePath",
            fixture.DecisionPackagePath,
            "-OutputRoot",
            fixture.OutputRoot);

        Assert.NotEqual(0, result.ExitCode);
        var summary = ReadSingleSummary(fixture.OutputRoot, "default-entry-readiness-summary.json");
        Assert.Contains(
            summary.RootElement.GetProperty("blockers").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "HumanDecisionNotApproved");
        Assert.Contains(
            summary.RootElement.GetProperty("blockers").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "ApproverMissing");
    }

    [Fact]
    public void Readiness_script_rejects_ready_package_without_wpf_fallback()
    {
        var fixture = CreateReadinessFixture(
            trialStatus: "ReadyForDefaultEntryReview",
            trialReady: true,
            p1Pending: [],
            wpfFallbackVerified: false,
            fullGate: true,
            includeHumanApproval: true);

        var result = RunPowerShell(
            GetScriptPath("TestAvaloniaDefaultEntryReadiness.ps1"),
            "-DecisionPackagePath",
            fixture.DecisionPackagePath,
            "-OutputRoot",
            fixture.OutputRoot);

        Assert.NotEqual(0, result.ExitCode);
        var summary = ReadSingleSummary(fixture.OutputRoot, "default-entry-readiness-summary.json");
        Assert.Contains(
            summary.RootElement.GetProperty("blockers").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "WpfFallbackNotVerified");
    }

    [Fact]
    public void Readiness_script_approves_complete_human_signed_materials()
    {
        var fixture = CreateReadinessFixture(
            trialStatus: "ReadyForDefaultEntryReview",
            trialReady: true,
            p1Pending: [],
            wpfFallbackVerified: true,
            fullGate: true,
            includeHumanApproval: true);

        var result = RunPowerShell(
            GetScriptPath("TestAvaloniaDefaultEntryReadiness.ps1"),
            "-DecisionPackagePath",
            fixture.DecisionPackagePath,
            "-OutputRoot",
            fixture.OutputRoot);

        Assert.Equal(0, result.ExitCode);
        var summary = ReadSingleSummary(fixture.OutputRoot, "default-entry-readiness-summary.json");
        Assert.Equal("ApprovedForDefaultEntrySwitch", summary.RootElement.GetProperty("overallStatus").GetString());
        Assert.True(summary.RootElement.GetProperty("approvedForDefaultEntrySwitch").GetBoolean());
    }

    [Fact]
    public void Switch_script_generates_preview_report_without_modifying_launcher_profiles()
    {
        var fixture = CreateReadinessFixture(
            trialStatus: "ReadyForDefaultEntryReview",
            trialReady: true,
            p1Pending: [],
            wpfFallbackVerified: true,
            fullGate: true,
            includeHumanApproval: true);
        var readinessResult = RunPowerShell(
            GetScriptPath("TestAvaloniaDefaultEntryReadiness.ps1"),
            "-DecisionPackagePath",
            fixture.DecisionPackagePath,
            "-OutputRoot",
            fixture.OutputRoot);
        Assert.Equal(0, readinessResult.ExitCode);

        var readinessSummaryPath = Directory.GetFiles(
            fixture.OutputRoot,
            "default-entry-readiness-summary.json",
            SearchOption.AllDirectories).Single();
        var releaseRoot = Path.Combine(fixture.TempRoot, "release");
        var profilesPath = Path.Combine(releaseRoot, "avalonia-launcher", "launcher.profiles.json");
        Directory.CreateDirectory(Path.GetDirectoryName(profilesPath)!);
        WriteUtf8(profilesPath, LauncherProfilesJson);
        var before = File.ReadAllText(profilesPath, Encoding.UTF8);

        var switchOutputRoot = Path.Combine(fixture.TempRoot, "switch");
        var switchResult = RunPowerShell(
            GetScriptPath("SwitchAvaloniaDefaultEntry.ps1"),
            "-ReadinessSummaryPath",
            readinessSummaryPath,
            "-ReleaseRoot",
            releaseRoot,
            "-OutputRoot",
            switchOutputRoot,
            "-Preview");

        Assert.Equal(0, switchResult.ExitCode);
        Assert.Equal(before, File.ReadAllText(profilesPath, Encoding.UTF8));
        var reportPath = Directory.GetFiles(switchOutputRoot, "default-entry-switch-preview.json", SearchOption.AllDirectories).Single();
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath, Encoding.UTF8));
        Assert.True(report.RootElement.GetProperty("previewOnly").GetBoolean());
        Assert.False(report.RootElement.GetProperty("wouldModifyFiles").GetBoolean());
        Assert.Equal("IIoT.Edge.Launcher.exe", report.RootElement.GetProperty("currentDefaultEntry").GetProperty("launcher").GetString());
        Assert.Equal("IIoT.Edge.Launcher.Avalonia.exe", report.RootElement.GetProperty("targetDefaultEntry").GetProperty("launcher").GetString());
    }

    [Fact]
    public void Switch_script_apply_requires_approved_readiness_and_does_not_modify_when_rejected()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-switch-rejected-" + Guid.NewGuid().ToString("N"));
        var readinessSummaryPath = Path.Combine(tempRoot, "default-entry-readiness-summary.json");
        var releaseRoot = CreateReleaseRoot(tempRoot);
        var profilesPath = Path.Combine(releaseRoot, "avalonia-launcher", "launcher.profiles.json");
        var before = File.ReadAllText(profilesPath, Encoding.UTF8);
        Directory.CreateDirectory(tempRoot);
        WriteUtf8(
            readinessSummaryPath,
            """
            {
              "overallStatus": "DefaultEntrySwitchRejected",
              "approvedForDefaultEntrySwitch": false
            }
            """);

        var result = RunPowerShell(
            GetScriptPath("SwitchAvaloniaDefaultEntry.ps1"),
            "-ReadinessSummaryPath",
            readinessSummaryPath,
            "-ReleaseRoot",
            releaseRoot,
            "-OutputRoot",
            Path.Combine(tempRoot, "switch"),
            "-Apply");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("ApprovedForDefaultEntrySwitch", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(profilesPath, Encoding.UTF8));
    }

    [Fact]
    public void Switch_script_apply_marks_default_profile_and_creates_rollback_snapshot()
    {
        var fixture = CreateReadinessFixture(
            trialStatus: "ReadyForDefaultEntryReview",
            trialReady: true,
            p1Pending: [],
            wpfFallbackVerified: true,
            fullGate: true,
            includeHumanApproval: true);
        var readinessResult = RunPowerShell(
            GetScriptPath("TestAvaloniaDefaultEntryReadiness.ps1"),
            "-DecisionPackagePath",
            fixture.DecisionPackagePath,
            "-OutputRoot",
            fixture.OutputRoot);
        Assert.Equal(0, readinessResult.ExitCode);

        var readinessSummaryPath = Directory.GetFiles(
            fixture.OutputRoot,
            "default-entry-readiness-summary.json",
            SearchOption.AllDirectories).Single();
        var releaseRoot = CreateReleaseRoot(fixture.TempRoot);
        var profilesPath = Path.Combine(releaseRoot, "avalonia-launcher", "launcher.profiles.json");
        var before = File.ReadAllText(profilesPath, Encoding.UTF8);
        var switchOutputRoot = Path.Combine(fixture.TempRoot, "switch");

        var applyResult = RunPowerShell(
            GetScriptPath("SwitchAvaloniaDefaultEntry.ps1"),
            "-ReadinessSummaryPath",
            readinessSummaryPath,
            "-ReleaseRoot",
            releaseRoot,
            "-OutputRoot",
            switchOutputRoot,
            "-ReportName",
            "ApplyDefaultEntry",
            "-Apply");

        Assert.Equal(0, applyResult.ExitCode);
        var applySummaryPath = Path.Combine(switchOutputRoot, "ApplyDefaultEntry", "default-entry-switch-apply-summary.json");
        Assert.True(File.Exists(applySummaryPath), applySummaryPath);
        using var profiles = JsonDocument.Parse(File.ReadAllText(profilesPath, Encoding.UTF8));
        var profileArray = profiles.RootElement.EnumerateArray().ToArray();
        var uiOnly = profileArray.Single(profile => profile.GetProperty("ProfileId").GetString() == "HomogenizationLineAvalonia");
        var runtime = profileArray.Single(profile => profile.GetProperty("ProfileId").GetString() == "HomogenizationLineAvaloniaRuntime");
        Assert.True(uiOnly.GetProperty("IsDefault").GetBoolean());
        Assert.Equal("ProductionDefault", uiOnly.GetProperty("DefaultEntryRole").GetString());
        Assert.False(runtime.GetProperty("IsDefault").GetBoolean());

        var rollbackRoot = Path.Combine(switchOutputRoot, "ApplyDefaultEntry", "rollback-snapshot");
        Assert.True(File.Exists(Path.Combine(rollbackRoot, "launcher.profiles.json")));
        Assert.True(File.Exists(Path.Combine(rollbackRoot, "release-manifest.json")));
        Assert.True(File.Exists(Path.Combine(rollbackRoot, "candidate-validation-summary.json")));
        Assert.True(File.Exists(Path.Combine(rollbackRoot, "default-entry-readiness-summary.json")));
        Assert.True(File.Exists(Path.Combine(rollbackRoot, "default-entry-switch-apply-summary.json")));
        Assert.NotEqual(before, File.ReadAllText(profilesPath, Encoding.UTF8));

        using var applySummary = JsonDocument.Parse(File.ReadAllText(applySummaryPath, Encoding.UTF8));
        Assert.True(applySummary.RootElement.GetProperty("applyMode").GetBoolean());
        Assert.True(applySummary.RootElement.GetProperty("wouldModifyFiles").GetBoolean());
    }

    [Fact]
    public void Restore_script_restores_launcher_profile_from_rollback_snapshot()
    {
        var fixture = CreateReadinessFixture(
            trialStatus: "ReadyForDefaultEntryReview",
            trialReady: true,
            p1Pending: [],
            wpfFallbackVerified: true,
            fullGate: true,
            includeHumanApproval: true);
        var readinessResult = RunPowerShell(
            GetScriptPath("TestAvaloniaDefaultEntryReadiness.ps1"),
            "-DecisionPackagePath",
            fixture.DecisionPackagePath,
            "-OutputRoot",
            fixture.OutputRoot);
        Assert.Equal(0, readinessResult.ExitCode);

        var readinessSummaryPath = Directory.GetFiles(
            fixture.OutputRoot,
            "default-entry-readiness-summary.json",
            SearchOption.AllDirectories).Single();
        var releaseRoot = CreateReleaseRoot(fixture.TempRoot);
        var profilesPath = Path.Combine(releaseRoot, "avalonia-launcher", "launcher.profiles.json");
        var before = File.ReadAllText(profilesPath, Encoding.UTF8);
        var switchOutputRoot = Path.Combine(fixture.TempRoot, "switch");
        var applyResult = RunPowerShell(
            GetScriptPath("SwitchAvaloniaDefaultEntry.ps1"),
            "-ReadinessSummaryPath",
            readinessSummaryPath,
            "-ReleaseRoot",
            releaseRoot,
            "-OutputRoot",
            switchOutputRoot,
            "-ReportName",
            "ApplyDefaultEntry",
            "-Apply");
        Assert.Equal(0, applyResult.ExitCode);

        var rollbackRoot = Path.Combine(switchOutputRoot, "ApplyDefaultEntry", "rollback-snapshot");
        var restoreOutputRoot = Path.Combine(fixture.TempRoot, "restore");
        var restoreResult = RunPowerShell(
            GetScriptPath("RestoreAvaloniaDefaultEntry.ps1"),
            "-RollbackSnapshotPath",
            rollbackRoot,
            "-ReleaseRoot",
            releaseRoot,
            "-OutputRoot",
            restoreOutputRoot);

        Assert.Equal(0, restoreResult.ExitCode);
        Assert.Equal(before, File.ReadAllText(profilesPath, Encoding.UTF8));
        var restoreSummaryPath = Directory.GetFiles(restoreOutputRoot, "default-entry-restore-summary.json", SearchOption.AllDirectories).Single();
        using var restoreSummary = JsonDocument.Parse(File.ReadAllText(restoreSummaryPath, Encoding.UTF8));
        Assert.Equal(profilesPath, restoreSummary.RootElement.GetProperty("restoredFile").GetString());
    }

    [Fact]
    public void Switch_script_rejects_repeated_apply_without_restore()
    {
        var fixture = CreateReadinessFixture(
            trialStatus: "ReadyForDefaultEntryReview",
            trialReady: true,
            p1Pending: [],
            wpfFallbackVerified: true,
            fullGate: true,
            includeHumanApproval: true);
        var readinessResult = RunPowerShell(
            GetScriptPath("TestAvaloniaDefaultEntryReadiness.ps1"),
            "-DecisionPackagePath",
            fixture.DecisionPackagePath,
            "-OutputRoot",
            fixture.OutputRoot);
        Assert.Equal(0, readinessResult.ExitCode);

        var readinessSummaryPath = Directory.GetFiles(
            fixture.OutputRoot,
            "default-entry-readiness-summary.json",
            SearchOption.AllDirectories).Single();
        var releaseRoot = CreateReleaseRoot(fixture.TempRoot);
        var firstApply = RunPowerShell(
            GetScriptPath("SwitchAvaloniaDefaultEntry.ps1"),
            "-ReadinessSummaryPath",
            readinessSummaryPath,
            "-ReleaseRoot",
            releaseRoot,
            "-OutputRoot",
            Path.Combine(fixture.TempRoot, "switch-1"),
            "-Apply");
        Assert.Equal(0, firstApply.ExitCode);

        var secondApply = RunPowerShell(
            GetScriptPath("SwitchAvaloniaDefaultEntry.ps1"),
            "-ReadinessSummaryPath",
            readinessSummaryPath,
            "-ReleaseRoot",
            releaseRoot,
            "-OutputRoot",
            Path.Combine(fixture.TempRoot, "switch-2"),
            "-Apply");

        Assert.NotEqual(0, secondApply.ExitCode);
        Assert.Contains("已存在默认 profile", secondApply.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_script_rejects_missing_snapshot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-restore-missing-" + Guid.NewGuid().ToString("N"));
        var releaseRoot = CreateReleaseRoot(tempRoot);

        var result = RunPowerShell(
            GetScriptPath("RestoreAvaloniaDefaultEntry.ps1"),
            "-RollbackSnapshotPath",
            Path.Combine(tempRoot, "missing-snapshot"),
            "-ReleaseRoot",
            releaseRoot,
            "-OutputRoot",
            Path.Combine(tempRoot, "restore"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("snapshot 不存在", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Switch_script_rejects_non_approved_readiness_summary()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-switch-" + Guid.NewGuid().ToString("N"));
        var readinessSummaryPath = Path.Combine(tempRoot, "default-entry-readiness-summary.json");
        var releaseRoot = Path.Combine(tempRoot, "release");
        Directory.CreateDirectory(tempRoot);
        WriteUtf8(
            readinessSummaryPath,
            """
            {
              "overallStatus": "DefaultEntrySwitchRejected",
              "approvedForDefaultEntrySwitch": false
            }
            """);

        var result = RunPowerShell(
            GetScriptPath("SwitchAvaloniaDefaultEntry.ps1"),
            "-ReadinessSummaryPath",
            readinessSummaryPath,
            "-ReleaseRoot",
            releaseRoot,
            "-OutputRoot",
            Path.Combine(tempRoot, "switch"),
            "-Preview");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("ApprovedForDefaultEntrySwitch", result.Output, StringComparison.Ordinal);
    }

    private static ReadinessFixture CreateReadinessFixture(
        string trialStatus,
        bool trialReady,
        string[] p1Pending,
        bool wpfFallbackVerified,
        bool fullGate,
        bool includeHumanApproval)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-readiness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var candidatePath = Path.Combine(tempRoot, "candidate-validation-summary.json");
        var trialPath = Path.Combine(tempRoot, "trial-review-summary.json");
        var acceptancePath = Path.Combine(tempRoot, "Avalonia12-现场试运行验收记录.md");
        var decisionPath = Path.Combine(tempRoot, "default-entry-decision-package.json");
        var outputRoot = Path.Combine(tempRoot, "readiness");

        WriteUtf8(
            candidatePath,
            $$"""
            {
              "fullGate": {{JsonSerializer.Serialize(fullGate)}},
              "wpfFallback": { "verified": {{JsonSerializer.Serialize(wpfFallbackVerified)}} }
            }
            """);
        WriteUtf8(
            trialPath,
            $$"""
            {
              "overallStatus": "{{trialStatus}}",
              "readyForDefaultEntryReview": {{JsonSerializer.Serialize(trialReady)}},
              "p0Blockers": [],
              "p1Pending": {{JsonSerializer.Serialize(p1Pending)}}
            }
            """);
        WriteUtf8(
            acceptancePath,
            """
            # Avalonia 现场试运行验收记录

            验收记录状态：已完成

            | 项目 | 内容 |
            | --- | --- |
            | 是否允许进入切默认入口评审 | 是 |
            """);

        var finalDecisionJson = includeHumanApproval
            ? """
              {
                "allowedToSwitchDefaultEntry": true,
                "approver": "现场负责人",
                "decidedAt": "2026-05-14T15:30:00+08:00",
                "rollbackOwner": "回退负责人",
                "notes": "允许进入后续独立切换批次。"
              }
              """
            : """
              {
                "allowedToSwitchDefaultEntry": null,
                "approver": null,
                "decidedAt": null,
                "rollbackOwner": null,
                "notes": "等待人工签字。"
              }
              """;

        WriteUtf8(
            decisionPath,
            $$"""
            {
              "readyForDefaultEntryReview": {{JsonSerializer.Serialize(trialReady)}},
              "finalDecision": {{finalDecisionJson}},
              "inputs": {
                "candidateSummaryPath": "{{JsonEscape(candidatePath)}}",
                "trialReviewSummaryPath": "{{JsonEscape(trialPath)}}",
                "acceptanceRecordPath": "{{JsonEscape(acceptancePath)}}"
              }
            }
            """);

        return new ReadinessFixture(tempRoot, decisionPath, outputRoot);
    }

    private static JsonDocument ReadSingleSummary(string outputRoot, string fileName)
    {
        var summaryPath = Directory.GetFiles(outputRoot, fileName, SearchOption.AllDirectories).Single();
        return JsonDocument.Parse(File.ReadAllText(summaryPath, Encoding.UTF8));
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

    private static string GetScriptPath(string scriptName)
        => Path.Combine(FindRepositoryRoot(), "scripts", scriptName);

    private static string CreateReleaseRoot(string tempRoot)
    {
        var releaseRoot = Path.Combine(tempRoot, "release");
        var launcherRoot = Path.Combine(releaseRoot, "avalonia-launcher");
        Directory.CreateDirectory(launcherRoot);
        WriteUtf8(Path.Combine(launcherRoot, "launcher.profiles.json"), LauncherProfilesJson);
        WriteUtf8(Path.Combine(releaseRoot, "release-manifest.json"), """{ "releaseKind": "AvaloniaMigration" }""");
        WriteUtf8(
            Path.Combine(releaseRoot, "candidate-validation-summary.json"),
            """
            {
              "fullGate": true,
              "wpfFallback": { "verified": true }
            }
            """);
        return releaseRoot;
    }

    private static void WriteUtf8(string path, string content)
        => File.WriteAllText(path, content, Encoding.UTF8);

    private static string JsonEscape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

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

    private const string LauncherProfilesJson = """
    [
      {
        "ProfileId": "HomogenizationLineAvalonia",
        "DisplayName": "匀浆线 Avalonia UI-only",
        "ExecutablePath": "..\\avalonia-shell\\IIoT.Edge.AvaloniaShell.exe"
      },
      {
        "ProfileId": "HomogenizationLineAvaloniaRuntime",
        "DisplayName": "匀浆线 Avalonia 运行联调",
        "ExecutablePath": "..\\avalonia-shell\\IIoT.Edge.AvaloniaShell.exe",
        "Arguments": [ "--start-runtime" ]
      }
    ]
    """;

    private sealed record ReadinessFixture(string TempRoot, string DecisionPackagePath, string OutputRoot);
}
