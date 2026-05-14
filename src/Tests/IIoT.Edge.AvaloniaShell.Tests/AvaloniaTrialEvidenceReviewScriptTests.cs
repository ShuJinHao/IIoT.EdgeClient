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
        Assert.Equal(0, summary.RootElement.GetProperty("p0Blockers").GetArrayLength());
        Assert.True(summary.RootElement.GetProperty("p1Pending").GetArrayLength() > 0);

        var reportPath = Directory.GetFiles(outputRoot, "trial-review-report.md", SearchOption.AllDirectories).Single();
        Assert.Contains("Avalonia 试运行证据审查报告", File.ReadAllText(reportPath, Encoding.UTF8), StringComparison.Ordinal);
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

    private static void CreateMinimalEvidencePackage(string evidenceRoot, bool includeReleaseManifest)
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
    }

    private static (int ExitCode, string Output) RunPowerShell(string scriptPath, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = "powershell";
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
