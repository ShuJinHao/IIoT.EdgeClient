using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;
using System.Diagnostics;

namespace IIoT.Edge.Launcher.Services;

public sealed class ShellLaunchService : IShellLaunchService, IDisposable
{
    private const string ShellProcessName = "IIoT.Edge.Shell";
    private static readonly TimeSpan DefaultShellReadinessTimeout = TimeSpan.FromMinutes(5);

    private readonly IProcessStarter _processStarter;
    private readonly IShellInstanceIdResolver _instanceIdResolver;
    private readonly IShellInstanceProbe _instanceProbe;
    private readonly ILauncherUpdateOperationGate _updateOperationGate;
    private readonly IEdgeUpdateTransactionRecovery? _updateTransactionRecovery;
    private readonly Action<Process> _terminateProcess;
    private readonly Func<CancellationToken, Task> _readinessDeadline;
    private readonly CancellationTokenSource _disposeCts = new();
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
            TryTerminate,
            readinessDeadline: null)
    {
    }

    internal ShellLaunchService(
        IProcessStarter processStarter,
        IShellInstanceIdResolver instanceIdResolver,
        IShellInstanceProbe instanceProbe,
        ILauncherUpdateOperationGate? updateOperationGate,
        IEdgeUpdateTransactionRecovery? updateTransactionRecovery,
        Action<Process> terminateProcess,
        Func<CancellationToken, Task>? readinessDeadline = null)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        _instanceIdResolver = instanceIdResolver ?? throw new ArgumentNullException(nameof(instanceIdResolver));
        _instanceProbe = instanceProbe ?? throw new ArgumentNullException(nameof(instanceProbe));
        _updateOperationGate = updateOperationGate ?? NoopLauncherUpdateOperationGate.Instance;
        _updateTransactionRecovery = updateTransactionRecovery;
        _terminateProcess = terminateProcess
            ?? throw new ArgumentNullException(nameof(terminateProcess));
        _readinessDeadline = readinessDeadline
            ?? (cancellationToken => Task.Delay(
                DefaultShellReadinessTimeout,
                cancellationToken));
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

    public async Task LaunchAsync(
        LauncherProfileDefinition profile,
        CancellationToken cancellationToken = default)
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

        if (readiness is null)
        {
            TrackProcess(profile.MachineProfile, process);
            return;
        }

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCts.Token);
        var processHandled = false;
        try
        {
            var outcomeTask = readiness.WaitAsync(waitCts.Token);
            var processExitTask = process.WaitForExitAsync(waitCts.Token);
            var deadlineTask = _readinessDeadline(waitCts.Token);
            var completed = await Task.WhenAny(
                    outcomeTask,
                    processExitTask,
                    deadlineTask)
                .ConfigureAwait(false);
            waitCts.Token.ThrowIfCancellationRequested();
            if (completed == deadlineTask)
            {
                await deadlineTask.ConfigureAwait(false);
                waitCts.Cancel();
                TerminateIncompleteLaunch(process);
                processHandled = true;
                throw new InvalidOperationException(
                    $"客户端启动失败：{profile.DisplayName} 未在允许的启动窗口内完成就绪握手。");
            }

            if (completed == processExitTask)
            {
                await processExitTask.ConfigureAwait(false);
                process.Dispose();
                processHandled = true;
                throw new InvalidOperationException(
                    $"客户端启动失败：{profile.DisplayName} 在完成启动握手前退出。");
            }

            var outcome = await outcomeTask.ConfigureAwait(false);
            waitCts.Cancel();
            if (!string.Equals(
                    outcome.MachineProfile,
                    profile.MachineProfile,
                    StringComparison.OrdinalIgnoreCase))
            {
                TerminateIncompleteLaunch(process);
                processHandled = true;
                throw new InvalidOperationException(
                    $"客户端启动失败：{profile.DisplayName} 返回了不匹配的工序身份。");
            }

            if (string.Equals(
                    outcome.Status,
                    EdgeClientShellLaunchStatuses.Failed,
                    StringComparison.Ordinal))
            {
                TrackProcessIfRunning(profile.MachineProfile, process);
                processHandled = true;
                throw new InvalidOperationException(
                    $"客户端启动失败：{profile.DisplayName}；{outcome.Message}");
            }

            var activeModuleIds = outcome.ActiveModuleIds.ToHashSet(
                StringComparer.OrdinalIgnoreCase);
            var missingExpectedModules = profile.ExpectedModuleIds
                .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
                .Select(static moduleId => moduleId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(moduleId => !activeModuleIds.Contains(moduleId))
                .ToArray();
            if (missingExpectedModules.Length > 0)
            {
                TerminateIncompleteLaunch(process);
                processHandled = true;
                throw new InvalidOperationException(
                    $"客户端启动失败：{profile.DisplayName} 未激活目标模块 {string.Join(", ", missingExpectedModules)}。");
            }
            if (!IsRunning(process))
            {
                process.Dispose();
                processHandled = true;
                throw new InvalidOperationException(
                    $"客户端启动失败：{profile.DisplayName} 在报告就绪后退出。");
            }

            TrackProcess(profile.MachineProfile, process);
            processHandled = true;
        }
        catch (InvalidDataException ex)
        {
            if (!processHandled)
            {
                TerminateIncompleteLaunch(process);
            }

            throw new InvalidOperationException(
                $"客户端启动失败：{profile.DisplayName} 返回了无效的启动握手。",
                ex);
        }
        catch
        {
            if (!processHandled)
            {
                TerminateIncompleteLaunch(process);
            }

            throw;
        }
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        lock (_syncRoot)
        {
            foreach (var tracked in _startedProcesses)
            {
                tracked.Process.Dispose();
            }

            _startedProcesses.Clear();
        }
    }

    private void TrackProcessIfRunning(
        string machineProfile,
        Process process)
    {
        if (IsRunning(process))
        {
            TrackProcess(machineProfile, process);
            return;
        }

        process.Dispose();
    }

    private void TrackProcess(string machineProfile, Process process)
    {
        lock (_syncRoot)
        {
            _startedProcesses.Add(new TrackedShellProcess(machineProfile, process));
        }
    }

    private void TerminateIncompleteLaunch(Process process)
    {
        _terminateProcess(process);
        process.Dispose();
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
        private readonly TaskCompletionSource _changed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
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

        public async Task<EdgeClientShellLaunchOutcome> WaitAsync(
            CancellationToken cancellationToken)
        {
            if (EdgeClientUpdateCoordination.TryReadShellLaunchOutcome(
                    ReadyPath,
                    out var immediate))
            {
                return immediate;
            }

            await _changed.Task.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (EdgeClientUpdateCoordination.TryReadShellLaunchOutcome(
                    ReadyPath,
                    out var outcome))
            {
                return outcome;
            }

            throw new InvalidDataException("Shell 启动握手内容无效。");
        }

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnReadyFileChanged;
            _watcher.Changed -= OnReadyFileChanged;
            _watcher.Renamed -= OnReadyFileChanged;
            _watcher.Dispose();
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
            => _changed.TrySetResult();
    }

    private sealed record TrackedShellProcess(string MachineProfile, Process Process);
}
