using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;
using System.Diagnostics;

namespace IIoT.Edge.Launcher.Services;

public sealed class ShellLaunchService : IShellLaunchService, IDisposable
{
    private const string ShellProcessName = "IIoT.Edge.Shell";
    private static readonly TimeSpan ShellLaunchReadyTimeout =
        TimeSpan.FromSeconds(10);

    private readonly IProcessStarter _processStarter;
    private readonly IShellInstanceIdResolver _instanceIdResolver;
    private readonly IShellInstanceProbe _instanceProbe;
    private readonly ILauncherUpdateOperationGate _updateOperationGate;
    private readonly IEdgeUpdateTransactionRecovery? _updateTransactionRecovery;
    private readonly TimeSpan _shellLaunchReadyTimeout;
    private readonly Action<Process> _terminateProcess;
    private readonly object _syncRoot = new();
    private readonly List<TrackedShellProcess> _startedProcesses = [];

    public ShellLaunchService(
        IProcessStarter processStarter,
        IShellInstanceIdResolver instanceIdResolver,
        IShellInstanceProbe instanceProbe,
        ILauncherUpdateOperationGate? updateOperationGate = null,
        IEdgeUpdateTransactionRecovery? updateTransactionRecovery = null)
        : this(
            processStarter,
            instanceIdResolver,
            instanceProbe,
            updateOperationGate,
            updateTransactionRecovery,
            ShellLaunchReadyTimeout,
            TryTerminate)
    {
    }

    internal ShellLaunchService(
        IProcessStarter processStarter,
        IShellInstanceIdResolver instanceIdResolver,
        IShellInstanceProbe instanceProbe,
        ILauncherUpdateOperationGate? updateOperationGate,
        IEdgeUpdateTransactionRecovery? updateTransactionRecovery,
        TimeSpan shellLaunchReadyTimeout,
        Action<Process> terminateProcess)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        _instanceIdResolver = instanceIdResolver ?? throw new ArgumentNullException(nameof(instanceIdResolver));
        _instanceProbe = instanceProbe ?? throw new ArgumentNullException(nameof(instanceProbe));
        _updateOperationGate = updateOperationGate ?? NoopLauncherUpdateOperationGate.Instance;
        _updateTransactionRecovery = updateTransactionRecovery;
        _shellLaunchReadyTimeout = shellLaunchReadyTimeout > TimeSpan.Zero
            ? shellLaunchReadyTimeout
            : throw new ArgumentOutOfRangeException(
                nameof(shellLaunchReadyTimeout));
        _terminateProcess = terminateProcess
            ?? throw new ArgumentNullException(nameof(terminateProcess));
    }

    public bool HasAnyRunningShellProcess()
        => HasTrackedRunningShellProcess() || HasOperatingSystemShellProcess();

    public bool IsProfileRunning(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (HasTrackedRunningShellProcess(profile.MachineProfile))
        {
            return true;
        }

        var instanceId = _instanceIdResolver.ResolveInstanceId(profile);
        return !string.IsNullOrWhiteSpace(instanceId)
               && _instanceProbe.IsInstanceRunning(instanceId);
    }

    public void Launch(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using var launchLease = _updateOperationGate.TryAcquire();
        if (launchLease is null)
        {
            throw new InvalidOperationException("更新正在进行，暂时不能启动客户端。");
        }

        if (_updateTransactionRecovery?.IsProfileBlocked(profile.MachineProfile) == true)
        {
            throw new InvalidOperationException(
                $"更新事务恢复失败，工序 {profile.DisplayName} 暂时不能启动；请保留诊断证据并人工恢复。");
        }

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
        var readyPath = _updateOperationGate.CreateShellLaunchReadyPath();
        using var readiness = string.IsNullOrWhiteSpace(readyPath)
            ? null
            : new ShellLaunchReadinessSignal(readyPath);
        if (readiness is not null)
        {
            startInfo.EnvironmentVariables[
                EdgeClientUpdateCoordination.ShellLaunchReadyEnvironmentVariable] =
                readiness.ReadyPath;
        }

        var process = _processStarter.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"客户端启动失败：{profile.DisplayName}");
        }

        if (readiness is not null
            && !readiness.Wait(_shellLaunchReadyTimeout))
        {
            _terminateProcess(process);
            process.Dispose();
            throw new InvalidOperationException(
                $"客户端启动失败：{profile.DisplayName} 未完成安全启动握手。");
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

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(milliseconds: 5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or System.ComponentModel.Win32Exception
                                       or NotSupportedException)
        {
        }
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

    private sealed class ShellLaunchReadinessSignal : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new(initialState: false);
        private readonly FileSystemWatcher _watcher;

        public ShellLaunchReadinessSignal(string readyPath)
        {
            ReadyPath = Path.GetFullPath(readyPath);
            var directory = Path.GetDirectoryName(ReadyPath)
                ?? throw new InvalidOperationException(
                    "Shell 启动握手文件缺少目录。");
            Directory.CreateDirectory(directory);
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(ReadyPath))
            {
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.CreationTime
                               | NotifyFilters.LastWrite
            };
            _watcher.Created += OnReadyFileChanged;
            _watcher.Changed += OnReadyFileChanged;
            _watcher.Renamed += OnReadyFileChanged;
            _watcher.EnableRaisingEvents = true;
        }

        public string ReadyPath { get; }

        public bool Wait(TimeSpan timeout)
        {
            if (File.Exists(ReadyPath))
            {
                return true;
            }

            return _ready.Wait(timeout) && File.Exists(ReadyPath);
        }

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnReadyFileChanged;
            _watcher.Changed -= OnReadyFileChanged;
            _watcher.Renamed -= OnReadyFileChanged;
            _watcher.Dispose();
            _ready.Dispose();
            try
            {
                if (File.Exists(ReadyPath))
                {
                    File.Delete(ReadyPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void OnReadyFileChanged(object sender, FileSystemEventArgs args)
            => _ready.Set();
    }

    private sealed record TrackedShellProcess(string MachineProfile, Process Process);
}
