using IIoT.Edge.Launcher.Models;
using System.Diagnostics;

namespace IIoT.Edge.Launcher.Services;

public sealed class ShellLaunchService : IShellLaunchService
{
    private readonly IProcessStarter _processStarter;

    public ShellLaunchService(IProcessStarter processStarter)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
    }

    public void Launch(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var launchTarget = ShellLaunchTargetResolver.Resolve(profile.ExecutablePath);
        var startInfo = new ProcessStartInfo(launchTarget.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = launchTarget.WorkingDirectory
        };

        foreach (var argument in launchTarget.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.EnvironmentVariables["Shell__MachineProfile"] = profile.MachineProfile;

        var process = _processStarter.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"客户端启动失败：{profile.DisplayName}");
        }
    }
}
