using IIoT.Edge.Launcher.Models;
using System.Diagnostics;

namespace IIoT.Edge.Launcher.Services;

public sealed class ShellLaunchService : IShellLaunchService, IDisposable
{
    private const string ShellProcessName = "IIoT.Edge.Shell";

    private readonly IProcessStarter _processStarter;
    private readonly object _syncRoot = new();
    private readonly List<TrackedShellProcess> _startedProcesses = [];

    public ShellLaunchService(IProcessStarter processStarter)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
    }

    public bool HasAnyRunningShellProcess()
        => HasTrackedRunningShellProcess() || HasOperatingSystemShellProcess();

    public bool IsProfileRunning(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return HasTrackedRunningShellProcess(profile.MachineProfile);
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

        lock (_syncRoot)
        {
            _startedProcesses.Add(new TrackedShellProcess(profile.MachineProfile, process));
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            foreach (var tracked in _startedProcesses)
            {
                tracked.Process.Dispose();
            }

            _startedProcesses.Clear();
        }
    }

    private bool HasTrackedRunningShellProcess()
        => HasTrackedRunningShellProcess(machineProfile: null);

    private bool HasTrackedRunningShellProcess(string? machineProfile)
    {
        lock (_syncRoot)
        {
            var hasRunningProcess = false;
            for (var i = _startedProcesses.Count - 1; i >= 0; i--)
            {
                var tracked = _startedProcesses[i];
                var process = tracked.Process;
                if (IsRunning(process))
                {
                    if (string.IsNullOrWhiteSpace(machineProfile)
                        || string.Equals(tracked.MachineProfile, machineProfile, StringComparison.OrdinalIgnoreCase))
                    {
                        hasRunningProcess = true;
                    }

                    continue;
                }

                process.Dispose();
                _startedProcesses.RemoveAt(i);
            }

            return hasRunningProcess;
        }
    }

    private static bool HasOperatingSystemShellProcess()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(ShellProcessName))
            {
                using (process)
                {
                    if (IsRunning(process))
                    {
                        return true;
                    }
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        return false;
    }

    private static bool IsRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record TrackedShellProcess(string MachineProfile, Process Process);
}
