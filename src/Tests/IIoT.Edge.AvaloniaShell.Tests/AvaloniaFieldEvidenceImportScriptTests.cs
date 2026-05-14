using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class AvaloniaFieldEvidenceImportScriptTests
{
    [Fact]
    public void Import_script_imports_zip_evidence_and_keeps_readiness_rejected_without_signature()
    {
        var fixture = CreateEvidenceFixture(includeAcceptanceRecord: true, includeScreenshots: true, includeSignedDecision: false);
        var zipPath = Path.Combine(fixture.TempRoot, "field-evidence.zip");
        ZipFile.CreateFromDirectory(fixture.EvidenceRoot, zipPath);

        var result = RunPowerShell(
            GetScriptPath("ImportAvaloniaFieldEvidence.ps1"),
            "-EvidencePath",
            zipPath,
            "-OutputRoot",
            fixture.OutputRoot,
            "-ImportName",
            "ImportZip");

        Assert.Equal(0, result.ExitCode);
        using var summary = ReadImportSummary(fixture.OutputRoot, "ImportZip");
        var root = summary.RootElement;
        Assert.Equal("ReadyForDefaultEntryReview", root.GetProperty("reviewStatus").GetString());
        Assert.Equal("DefaultEntrySwitchRejected", root.GetProperty("readinessStatus").GetString());
        Assert.Equal("Skipped", root.GetProperty("switchPreviewStatus").GetString());
        Assert.Equal(0, root.GetProperty("missingItems").GetArrayLength());
        Assert.True(File.Exists(root.GetProperty("inventoryPath").GetString()));
    }

    [Fact]
    public void Import_script_keeps_p1_pending_when_acceptance_or_screenshots_are_missing()
    {
        var fixture = CreateEvidenceFixture(includeAcceptanceRecord: false, includeScreenshots: false, includeSignedDecision: false);

        var result = RunPowerShell(
            GetScriptPath("ImportAvaloniaFieldEvidence.ps1"),
            "-EvidencePath",
            fixture.EvidenceRoot,
            "-OutputRoot",
            fixture.OutputRoot,
            "-ImportName",
            "ImportMissing");

        Assert.Equal(0, result.ExitCode);
        using var summary = ReadImportSummary(fixture.OutputRoot, "ImportMissing");
        var root = summary.RootElement;
        Assert.Equal("P1Pending", root.GetProperty("reviewStatus").GetString());
        Assert.Equal("DefaultEntrySwitchRejected", root.GetProperty("readinessStatus").GetString());
        Assert.Contains(root.GetProperty("missingItems").EnumerateArray(), item =>
            item.GetString() == "docs\\Avalonia12-现场试运行验收记录.md");
        Assert.Contains(root.GetProperty("missingItems").EnumerateArray(), item =>
            item.GetString() == "Diagnostics 摘要截图");
    }

    [Fact]
    public void Import_script_generates_switch_preview_for_complete_evidence_with_signed_decision_package()
    {
        var fixture = CreateEvidenceFixture(includeAcceptanceRecord: true, includeScreenshots: true, includeSignedDecision: true);

        var result = RunPowerShell(
            GetScriptPath("ImportAvaloniaFieldEvidence.ps1"),
            "-EvidencePath",
            fixture.EvidenceRoot,
            "-OutputRoot",
            fixture.OutputRoot,
            "-ImportName",
            "ImportApproved",
            "-ReleaseRoot",
            fixture.ReleaseRoot);

        Assert.Equal(0, result.ExitCode);
        using var summary = ReadImportSummary(fixture.OutputRoot, "ImportApproved");
        var root = summary.RootElement;
        Assert.Equal("ReadyForDefaultEntryReview", root.GetProperty("reviewStatus").GetString());
        Assert.Equal("ApprovedForDefaultEntrySwitch", root.GetProperty("readinessStatus").GetString());
        Assert.Equal("Generated", root.GetProperty("switchPreviewStatus").GetString());
        var previewPath = root.GetProperty("switchPreviewPath").GetString();
        Assert.False(string.IsNullOrWhiteSpace(previewPath));
        Assert.True(File.Exists(previewPath), previewPath);
    }

    private static EvidenceFixture CreateEvidenceFixture(
        bool includeAcceptanceRecord,
        bool includeScreenshots,
        bool includeSignedDecision)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-evidence-import-" + Guid.NewGuid().ToString("N"));
        var evidenceRoot = Path.Combine(tempRoot, "evidence");
        var outputRoot = Path.Combine(tempRoot, "inbox");
        var releaseRoot = Path.Combine(tempRoot, "release");
        Directory.CreateDirectory(evidenceRoot);
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(Path.Combine(evidenceRoot, "launcher"));
        Directory.CreateDirectory(Path.Combine(evidenceRoot, "docs"));
        Directory.CreateDirectory(Path.Combine(evidenceRoot, "screenshots"));
        Directory.CreateDirectory(Path.Combine(releaseRoot, "avalonia-launcher"));

        WriteUtf8(Path.Combine(evidenceRoot, "release-manifest.json"), """{ "releaseKind": "AvaloniaMigration" }""");
        WriteUtf8(
            Path.Combine(evidenceRoot, "candidate-validation-summary.json"),
            """
            {
              "fullGate": true,
              "wpfFallback": { "verified": true },
              "previewPackages": []
            }
            """);
        WriteUtf8(Path.Combine(evidenceRoot, "field-evidence-summary.json"), """{ "script": "test" }""");
        WriteUtf8(Path.Combine(evidenceRoot, "diagnostics-summary.md"), "# Diagnostics 摘要");
        WriteUtf8(Path.Combine(evidenceRoot, "launcher", "launcher.profiles.json"), LauncherProfilesJson);
        WriteUtf8(Path.Combine(releaseRoot, "avalonia-launcher", "launcher.profiles.json"), LauncherProfilesJson);
        WriteUtf8(
            Path.Combine(evidenceRoot, "docs", "Avalonia12-现场试运行验收记录模板.md"),
            """
            # Avalonia 现场试运行验收记录

            验收记录状态：待填写
            """);
        WriteUtf8(
            Path.Combine(evidenceRoot, "docs", "Avalonia12-试运行问题回收清单.md"),
            """
            # Avalonia 试运行问题回收清单

            ReadyForDefaultEntryReview
            """);
        WriteUtf8(Path.Combine(evidenceRoot, "screenshots", "截图占位说明.md"), "# 截图占位说明");

        if (includeAcceptanceRecord)
        {
            WriteUtf8(
                Path.Combine(evidenceRoot, "docs", "Avalonia12-现场试运行验收记录.md"),
                """
                # Avalonia 现场试运行验收记录

                验收记录状态：已完成

                | 项目 | 内容 |
                | --- | --- |
                | 是否允许进入切默认入口评审 | 是 |
                """);
        }

        if (includeScreenshots)
        {
            foreach (var fileName in new[]
            {
                "01-diagnostics-summary.png",
                "02-io-write-gate.png",
                "03-plc-write-trace.png",
                "04-wpf-fallback.png"
            })
            {
                File.WriteAllBytes(Path.Combine(evidenceRoot, "screenshots", fileName), [0x89, 0x50, 0x4E, 0x47]);
            }
        }

        if (includeSignedDecision)
        {
            WriteUtf8(
                Path.Combine(evidenceRoot, "default-entry-decision-package.json"),
                """
                {
                  "finalDecision": {
                    "allowedToSwitchDefaultEntry": true,
                    "approver": "现场负责人",
                    "decidedAt": "2026-05-14T16:00:00+08:00",
                    "rollbackOwner": "回退负责人",
                    "notes": "允许进入后续独立切换批次。"
                  }
                }
                """);
        }

        return new EvidenceFixture(tempRoot, evidenceRoot, outputRoot, releaseRoot);
    }

    private static JsonDocument ReadImportSummary(string outputRoot, string importName)
    {
        var summaryPath = Path.Combine(outputRoot, importName, "evidence-import-summary.json");
        Assert.True(File.Exists(summaryPath), summaryPath);
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

    private sealed record EvidenceFixture(string TempRoot, string EvidenceRoot, string OutputRoot, string ReleaseRoot);
}
