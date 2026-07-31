using IIoT.Edge.SharedKernel.Configuration;
using Xunit;

namespace IIoT.Edge.Launcher.FilesystemTests;

public sealed class EdgeClientUpdateCoordinationTests
{
    [Fact]
    public void ShellLaunchOutcome_WhenReadyPayloadIsValid_ShouldRoundTripNormalizedIdentity()
    {
        var baseDirectory = CreateBaseDirectory();
        try
        {
            var path = EdgeClientUpdateCoordination.CreateShellLaunchReadyPath(
                baseDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var written =
                EdgeClientUpdateCoordination.TryWriteShellLaunchOutcomeToPath(
                    path,
                    new EdgeClientShellLaunchOutcome(
                        EdgeClientUpdateCoordination.ShellLaunchOutcomeSchemaVersion,
                        " READY ",
                        " DieCuttingAnodeLine ",
                        [" AP "],
                        Message: null)
                    {
                        ProcessId = 1234
                    },
                    baseDirectory);
            var read = EdgeClientUpdateCoordination.TryReadShellLaunchOutcome(
                path,
                out var outcome);

            Assert.True(written);
            Assert.True(read);
            Assert.Equal(EdgeClientShellLaunchStatuses.Ready, outcome.Status);
            Assert.Equal("DieCuttingAnodeLine", outcome.MachineProfile);
            Assert.Equal(["AP"], outcome.ActiveModuleIds);
            Assert.Equal(1234, outcome.ProcessId);
            Assert.Null(outcome.Message);
        }
        finally
        {
            DeleteRoot(baseDirectory);
        }
    }

    [Fact]
    public void ShellLaunchOutcome_WhenFailedPayloadHasNoDiagnostic_ShouldRejectWrite()
    {
        var baseDirectory = CreateBaseDirectory();
        try
        {
            var path = EdgeClientUpdateCoordination.CreateShellLaunchReadyPath(
                baseDirectory);

            var written =
                EdgeClientUpdateCoordination.TryWriteShellLaunchOutcomeToPath(
                    path,
                    new EdgeClientShellLaunchOutcome(
                        EdgeClientUpdateCoordination.ShellLaunchOutcomeSchemaVersion,
                        EdgeClientShellLaunchStatuses.Failed,
                        "DieCuttingAnodeLine",
                        [],
                        Message: null)
                    {
                        ProcessId = 1234
                    },
                    baseDirectory);

            Assert.False(written);
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteRoot(baseDirectory);
        }
    }

    [Fact]
    public void ShellLaunchOutcome_WhenPathIsOutsideLauncherDirectory_ShouldRejectWrite()
    {
        var baseDirectory = CreateBaseDirectory();
        var outsidePath = Path.Combine(
            Path.GetTempPath(),
            $".shell-launch-ready-{Guid.NewGuid():N}.signal");
        try
        {
            var written =
                EdgeClientUpdateCoordination.TryWriteShellLaunchOutcomeToPath(
                    outsidePath,
                    new EdgeClientShellLaunchOutcome(
                        EdgeClientUpdateCoordination.ShellLaunchOutcomeSchemaVersion,
                        EdgeClientShellLaunchStatuses.Ready,
                        "DieCuttingAnodeLine",
                        ["AP"],
                        Message: null)
                    {
                        ProcessId = 1234
                    },
                    baseDirectory);

            Assert.False(written);
            Assert.False(File.Exists(outsidePath));
        }
        finally
        {
            if (File.Exists(outsidePath))
            {
                File.Delete(outsidePath);
            }

            DeleteRoot(baseDirectory);
        }
    }

    [Fact]
    public void ShellLaunchOutcome_WhenPayloadIsMalformed_ShouldRejectRead()
    {
        var baseDirectory = CreateBaseDirectory();
        try
        {
            var path = EdgeClientUpdateCoordination.CreateShellLaunchReadyPath(
                baseDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ malformed");

            var read = EdgeClientUpdateCoordination.TryReadShellLaunchOutcome(
                path,
                out _);

            Assert.False(read);
        }
        finally
        {
            DeleteRoot(baseDirectory);
        }
    }

    [Fact]
    public void ShellLaunchOutcome_WhenReadyWithDiagnosticsIsValid_ShouldRoundTripSafePayload()
    {
        var baseDirectory = CreateBaseDirectory();
        try
        {
            var path = EdgeClientUpdateCoordination.CreateShellLaunchReadyPath(baseDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var candidate = new EdgeClientShellLaunchOutcome(
                EdgeClientUpdateCoordination.ShellLaunchOutcomeSchemaVersion,
                EdgeClientShellLaunchStatuses.ReadyWithDiagnostics,
                "DieCuttingAnodeLine",
                ["AP"],
                Message: null)
            {
                ProcessId = 4321,
                Diagnostics =
                [
                    new EdgeClientShellLaunchDiagnostic(
                        "STARTUP_MES_RETRY_TASK_FAILED",
                        "System.Diagnostics",
                        "AP")
                ]
            };

            Assert.True(EdgeClientUpdateCoordination.TryWriteShellLaunchOutcomeToPath(
                path,
                candidate,
                baseDirectory));
            Assert.True(EdgeClientUpdateCoordination.TryReadShellLaunchOutcome(path, out var outcome));

            Assert.Equal(4321, outcome.ProcessId);
            Assert.Equal(EdgeClientShellLaunchStatuses.ReadyWithDiagnostics, outcome.Status);
            var diagnostic = Assert.Single(outcome.Diagnostics);
            Assert.Equal("STARTUP_MES_RETRY_TASK_FAILED", diagnostic.ReasonCode);
            Assert.Equal("System.Diagnostics", diagnostic.RepairTarget);
            Assert.Equal("AP", diagnostic.ModuleId);
        }
        finally
        {
            DeleteRoot(baseDirectory);
        }
    }

    [Fact]
    public void ShellLaunchOutcome_WhenProcessIdentityIsMissing_ShouldRejectWrite()
    {
        var baseDirectory = CreateBaseDirectory();
        try
        {
            var path = EdgeClientUpdateCoordination.CreateShellLaunchReadyPath(baseDirectory);

            var written = EdgeClientUpdateCoordination.TryWriteShellLaunchOutcomeToPath(
                path,
                new EdgeClientShellLaunchOutcome(
                    EdgeClientUpdateCoordination.ShellLaunchOutcomeSchemaVersion,
                    EdgeClientShellLaunchStatuses.Ready,
                    "DieCuttingAnodeLine",
                    ["AP"],
                    Message: null),
                baseDirectory);

            Assert.False(written);
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteRoot(baseDirectory);
        }
    }

    private static string CreateBaseDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "edge-update-coordination-tests",
            Guid.NewGuid().ToString("N"),
            "launcher");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteRoot(string baseDirectory)
    {
        var root = Directory.GetParent(baseDirectory)?.FullName;
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
