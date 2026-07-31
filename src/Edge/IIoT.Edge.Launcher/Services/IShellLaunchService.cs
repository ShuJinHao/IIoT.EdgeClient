using IIoT.Edge.Launcher.Models;

namespace IIoT.Edge.Launcher.Services;

public sealed record ShellLaunchResult(
    bool ReadyWithDiagnostics,
    IReadOnlyList<IIoT.Edge.SharedKernel.Configuration.EdgeClientShellLaunchDiagnostic> Diagnostics);

public interface IShellLaunchService
{
    bool HasAnyRunningShellProcess();

    bool IsProfileRunning(LauncherProfileDefinition profile);

    Task<ShellLaunchResult> LaunchAsync(
        LauncherProfileDefinition profile,
        CancellationToken cancellationToken = default);
}
