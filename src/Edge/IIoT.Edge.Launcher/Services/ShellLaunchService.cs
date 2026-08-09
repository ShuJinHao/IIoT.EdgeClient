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
    private readonly IEdgeUpdateTransactionRecovery _updateTransactionRecovery;
    private readonly ILauncherEnabledPluginSelectionSource? _selectionSource;
    private readonly ILauncherPluginActivationSource? _activationSource;
    private readonly Action<Process> _terminateProcess;
    private readonly Func<CancellationToken, Task> _readinessDeadline;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly object _syncRoot = new();
    private readonly List<TrackedShellProcess> _startedProcesses = [];
    private readonly HashSet<string> _launchesInProgress = new(StringComparer.Ordinal);

    public ShellLaunchService(
        IProcessStarter processStarter,
        IShellInstanceIdResolver instanceIdResolver,
        IShellInstanceProbe instanceProbe,
        ILauncherUpdateOperationGate updateOperationGate,
        IEdgeUpdateTransactionRecovery updateTransactionRecovery,
        ILauncherEnabledPluginSelectionSource? selectionSource = null,
        ILauncherPluginActivationSource? activationSource = null)
        : this(
            processStarter,
            instanceIdResolver,
            instanceProbe,
            updateOperationGate,
            updateTransactionRecovery,
            TryTerminate,
            readinessDeadline: null,
            selectionSource: selectionSource,
            activationSource: activationSource)
    {
    }

    internal ShellLaunchService(
        IProcessStarter processStarter,
        IShellInstanceIdResolver instanceIdResolver,
        IShellInstanceProbe instanceProbe,
        ILauncherUpdateOperationGate updateOperationGate,
        IEdgeUpdateTransactionRecovery updateTransactionRecovery,
        Action<Process> terminateProcess,
        Func<CancellationToken, Task>? readinessDeadline = null,
        ILauncherEnabledPluginSelectionSource? selectionSource = null,
        ILauncherPluginActivationSource? activationSource = null)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        _instanceIdResolver = instanceIdResolver ?? throw new ArgumentNullException(nameof(instanceIdResolver));
        _instanceProbe = instanceProbe ?? throw new ArgumentNullException(nameof(instanceProbe));
        _updateOperationGate = updateOperationGate ?? throw new ArgumentNullException(nameof(updateOperationGate));
        _updateTransactionRecovery = updateTransactionRecovery ?? throw new ArgumentNullException(nameof(updateTransactionRecovery));
        _selectionSource = selectionSource;
        _activationSource = activationSource;
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
        var clientCode = ResolveLaunchIdentity(profile);
        if (HasTrackedRunningShellProcess(clientCode))
        {
            return true;
        }

        var instanceId = _instanceIdResolver.ResolveInstanceId(profile);
        return string.IsNullOrWhiteSpace(instanceId)
            ? !string.IsNullOrWhiteSpace(profile.ClientCode)
            : _instanceProbe.IsInstanceRunning(instanceId);
    }

    public async Task<ShellLaunchResult> LaunchAsync(
        LauncherProfileDefinition profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var clientCode = ResolveLaunchIdentity(profile);
        using var inProcessLease = TryAcquireInProcessLaunch(clientCode)
            ?? throw new InvalidOperationException(
                $"设备 {profile.DisplayName} 正在启动，请勿重复操作。");

        using var launchLease = _updateOperationGate.TryAcquire();
        if (launchLease is null)
        {
            throw new InvalidOperationException("更新正在进行，暂时不能启动客户端。");
        }

        EnsureProfileIsSelected(profile);

        var instanceId = _instanceIdResolver.ResolveInstanceId(profile);
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new InvalidOperationException(
                $"客户端启动已阻断：{profile.DisplayName} 的 ClientCode 运行配置缺失或冲突。");
        }
        if (HasTrackedRunningShellProcess(clientCode)
            || _instanceProbe.IsInstanceRunning(instanceId))
        {
            throw new InvalidOperationException(
                $"设备 {profile.DisplayName} 已在运行，同一 ClientCode 不允许重复启动。");
        }

        if (_updateTransactionRecovery.IsProfileBlocked(profile.MachineProfile))
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

        startInfo.EnvironmentVariables["Shell__MachineProfile"] = clientCode;
        startInfo.EnvironmentVariables["Shell__ClientCode"] = clientCode;
        if (!string.IsNullOrWhiteSpace(profile.MachineConfigPath))
        {
            startInfo.EnvironmentVariables["Shell__MachineConfigPath"] = profile.MachineConfigPath;
        }
        var readyPath = _updateOperationGate.CreateShellLaunchReadyPath();
        if (string.IsNullOrWhiteSpace(readyPath))
        {
            throw new InvalidOperationException("Launcher 更新门控未提供 Shell 启动握手路径。");
        }

        using var readiness = new ShellLaunchReadinessSignal(readyPath);
        startInfo.EnvironmentVariables[
            EdgeClientUpdateCoordination.ShellLaunchReadyEnvironmentVariable] =
            readiness.ReadyPath;

        var process = _processStarter.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"客户端启动失败：{profile.DisplayName}");
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
            if (outcome.ProcessId != process.Id)
            {
                TerminateIncompleteLaunch(process);
                processHandled = true;
                throw new InvalidOperationException(
                    $"客户端启动失败：{profile.DisplayName} 返回了不匹配的进程身份。");
            }

            if (!string.Equals(
                    outcome.MachineProfile,
                    clientCode,
                    StringComparison.Ordinal))
            {
                TerminateIncompleteLaunch(process);
                processHandled = true;
                throw new InvalidOperationException(
                    $"客户端启动失败：{profile.DisplayName} 返回了不匹配的工序身份。");
            }

            if (!string.IsNullOrWhiteSpace(profile.ClientCode)
                && (!string.Equals(outcome.ClientCode, clientCode, StringComparison.Ordinal)
                    || !string.Equals(outcome.ModuleId, profile.ActivationModuleId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(outcome.PluginVersion, profile.PluginVersion, StringComparison.Ordinal)
                    || !string.Equals(outcome.PackageSha256, profile.PackageSha256, StringComparison.OrdinalIgnoreCase)))
            {
                TerminateIncompleteLaunch(process);
                processHandled = true;
                throw new InvalidOperationException(
                    $"客户端启动失败：{profile.DisplayName} 返回的 ClientCode、插件版本或包摘要与安装 Binding 不一致。");
            }

            if (string.Equals(
                    outcome.Status,
                    EdgeClientShellLaunchStatuses.Failed,
                    StringComparison.Ordinal))
            {
                TrackProcessIfRunning(clientCode, process);
                processHandled = true;
                throw new InvalidOperationException(
                    $"客户端启动失败：{profile.DisplayName} 报告了受控启动失败。");
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

            TrackProcess(clientCode, process);
            processHandled = true;
            return new ShellLaunchResult(
                string.Equals(
                    outcome.Status,
                    EdgeClientShellLaunchStatuses.ReadyWithDiagnostics,
                    StringComparison.Ordinal),
                outcome.Diagnostics);
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

    private void EnsureProfileIsSelected(LauncherProfileDefinition profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ClientCode))
        {
            if (string.IsNullOrWhiteSpace(profile.ActivationModuleId)
                || string.IsNullOrWhiteSpace(profile.ActivationPluginDirectory)
                || !Directory.Exists(profile.ActivationPluginDirectory))
            {
                throw new InvalidOperationException(
                    $"客户端启动已阻断：{profile.DisplayName} 的设备插件目录或 ModuleId 不完整。");
            }

            return;
        }

        if (_selectionSource is null)
        {
            return;
        }

        var expectedModuleIds = profile.ExpectedModuleIds
            .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Select(static moduleId => moduleId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (expectedModuleIds.Length == 0)
        {
            return;
        }

        var selection = _selectionSource.Load();
        var missingSelections = expectedModuleIds
            .Where(moduleId => !selection.Contains(moduleId))
            .ToArray();
        if (!selection.ManifestIsValid || missingSelections.Length > 0)
        {
            throw CreateProfileSelectionChangedException(profile);
        }

        var activationModuleId = profile.ActivationModuleId.Trim();
        var activationPluginDirectory = profile.ActivationPluginDirectory.Trim();
        var hasActivationModule = activationModuleId.Length > 0;
        var hasActivationDirectory = activationPluginDirectory.Length > 0;
        var activationSource = _activationSource;
        if (!hasActivationModule && !hasActivationDirectory)
        {
            return;
        }

        if (!hasActivationModule
            || !hasActivationDirectory
            || !expectedModuleIds.Contains(
                activationModuleId,
                StringComparer.OrdinalIgnoreCase)
            || !selection.TryGetByPluginDirectory(
                activationPluginDirectory,
                out var selectedPlugin)
            || !string.Equals(
                selectedPlugin.ModuleId,
                activationModuleId,
                StringComparison.OrdinalIgnoreCase)
            || activationSource is null)
        {
            throw CreateProfileSelectionChangedException(profile);
        }

        var activationStillExists = activationSource.LoadActivations().Any(activation =>
            string.Equals(
                activation.ModuleId,
                activationModuleId,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                activation.ProfileId,
                profile.ProfileId,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                activation.ProfileId,
                profile.MachineProfile,
                StringComparison.OrdinalIgnoreCase)
            && LauncherEnabledPluginSelection.PluginDirectoryComparer.Equals(
                activation.PluginDirectory,
                activationPluginDirectory));
        if (!activationStillExists)
        {
            throw CreateProfileSelectionChangedException(profile);
        }
    }

    private static InvalidOperationException CreateProfileSelectionChangedException(
        LauncherProfileDefinition profile)
        => new(
            $"客户端启动已阻断：{profile.DisplayName} 不在当前启用工序清单中。");

    private IDisposable? TryAcquireInProcessLaunch(string clientCode)
    {
        lock (_syncRoot)
        {
            if (!_launchesInProgress.Add(clientCode))
            {
                return null;
            }
        }

        return new DelegateLease(() =>
        {
            lock (_syncRoot)
            {
                _launchesInProgress.Remove(clientCode);
            }
        });
    }

    private static string ResolveLaunchIdentity(LauncherProfileDefinition profile)
        => string.IsNullOrWhiteSpace(profile.ClientCode)
            ? EdgeClientProgramDataPaths.SanitizePathSegment(profile.MachineProfile)
            : EdgeClientIdentity.NormalizeClientCode(profile.ClientCode);

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
        string clientCode,
        Process process)
    {
        if (IsRunning(process))
        {
            TrackProcess(clientCode, process);
            return;
        }

        process.Dispose();
    }

    private void TrackProcess(string clientCode, Process process)
    {
        lock (_syncRoot)
        {
            _startedProcesses.Add(new TrackedShellProcess(clientCode, process));
        }
    }

    private void TerminateIncompleteLaunch(Process process)
    {
        _terminateProcess(process);
        process.Dispose();
    }

    private bool HasTrackedRunningShellProcess()
        => HasTrackedRunningShellProcess(clientCode: null);

    private bool HasTrackedRunningShellProcess(string? clientCode)
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
                    if (string.IsNullOrWhiteSpace(clientCode)
                        || string.Equals(tracked.ClientCode, clientCode, StringComparison.Ordinal))
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

    private sealed record TrackedShellProcess(string ClientCode, Process Process);

    private sealed class DelegateLease(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
