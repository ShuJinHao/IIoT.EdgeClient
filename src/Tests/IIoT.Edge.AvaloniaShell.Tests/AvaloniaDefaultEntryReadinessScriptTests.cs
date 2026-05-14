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
