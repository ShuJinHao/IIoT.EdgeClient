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
        Assert.Contains("diagnostics", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet ef", script, StringComparison.OrdinalIgnoreCase);
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
