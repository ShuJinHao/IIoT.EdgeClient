using System.Text.Json;

namespace IIoT.Edge.SharedKernel.Configuration;

public static class EdgeClientShellLaunchStatuses
{
    public const string Ready = "ready";
    public const string ReadyWithDiagnostics = "readyWithDiagnostics";
    public const string Failed = "failed";
}

public sealed record EdgeClientShellLaunchDiagnostic(
    string ReasonCode,
    string RepairTarget,
    string? ModuleId = null);

public sealed record EdgeClientShellLaunchOutcome(
    int SchemaVersion,
    string Status,
    string MachineProfile,
    IReadOnlyList<string> ActiveModuleIds,
    string? Message)
{
    public int ProcessId { get; init; }

    public IReadOnlyList<EdgeClientShellLaunchDiagnostic> Diagnostics { get; init; } = [];

    public string ClientCode { get; init; } = string.Empty;

    public string ModuleId { get; init; } = string.Empty;

    public string PluginVersion { get; init; } = string.Empty;

    public string PackageSha256 { get; init; } = string.Empty;
}

public static class EdgeClientUpdateCoordination
{
    public const int ShellLaunchOutcomeSchemaVersion = 2;
    public const string UpdateOperationLockFileName = "update-operation.lock";
    public const string ShellPresenceLockFileName = "shell-presence.lock";
    public const string ShellLaunchReadyEnvironmentVariable =
        "IIOT_EDGE_SHELL_LAUNCH_READY_PATH";

    private const string ShellLaunchReadyFilePrefix = ".shell-launch-ready-";
    private const string ShellLaunchReadyFileSuffix = ".signal";
    private const int MaximumShellLaunchOutcomeBytes = 64 * 1024;
    private static readonly JsonSerializerOptions ShellLaunchJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string ResolveUpdateOperationLockPath(string? baseDirectory = null)
        => Path.Combine(
            EdgeClientProgramDataPaths.ResolveLauncherDirectory(baseDirectory),
            UpdateOperationLockFileName);

    public static string ResolveShellPresenceLockPath(string? baseDirectory = null)
        => Path.Combine(
            EdgeClientProgramDataPaths.ResolveLauncherDirectory(baseDirectory),
            ShellPresenceLockFileName);

    public static string CreateShellLaunchReadyPath(string? baseDirectory = null)
        => Path.Combine(
            EdgeClientProgramDataPaths.ResolveLauncherDirectory(baseDirectory),
            $"{ShellLaunchReadyFilePrefix}{Guid.NewGuid():N}{ShellLaunchReadyFileSuffix}");

    public static IDisposable? TryAcquireShellPresence(
        string? baseDirectory = null)
        => TryOpenShellPresence(
            baseDirectory,
            FileAccess.Read,
            FileShare.Read);

    public static IDisposable? TryAcquireExclusiveShellPresence(
        string? baseDirectory = null)
        => TryOpenShellPresence(
            baseDirectory,
            FileAccess.ReadWrite,
            FileShare.None);

    public static bool TrySignalShellLaunchReady(
        string machineProfile,
        IReadOnlyList<string> activeModuleIds,
        string? baseDirectory = null,
        string? clientCode = null,
        string? moduleId = null,
        string? pluginVersion = null,
        string? packageSha256 = null)
        => TrySignalShellLaunchOutcome(
            new EdgeClientShellLaunchOutcome(
                ShellLaunchOutcomeSchemaVersion,
                EdgeClientShellLaunchStatuses.Ready,
                machineProfile,
                activeModuleIds,
                Message: null)
            {
                ProcessId = Environment.ProcessId,
                ClientCode = clientCode ?? string.Empty,
                ModuleId = moduleId ?? string.Empty,
                PluginVersion = pluginVersion ?? string.Empty,
                PackageSha256 = packageSha256 ?? string.Empty
            },
            baseDirectory);

    public static bool TrySignalShellLaunchReadyWithDiagnostics(
        string machineProfile,
        IReadOnlyList<string> activeModuleIds,
        IReadOnlyList<EdgeClientShellLaunchDiagnostic> diagnostics,
        string? baseDirectory = null,
        string? clientCode = null,
        string? moduleId = null,
        string? pluginVersion = null,
        string? packageSha256 = null)
        => TrySignalShellLaunchOutcome(
            new EdgeClientShellLaunchOutcome(
                ShellLaunchOutcomeSchemaVersion,
                EdgeClientShellLaunchStatuses.ReadyWithDiagnostics,
                machineProfile,
                activeModuleIds,
                Message: null)
            {
                ProcessId = Environment.ProcessId,
                Diagnostics = diagnostics,
                ClientCode = clientCode ?? string.Empty,
                ModuleId = moduleId ?? string.Empty,
                PluginVersion = pluginVersion ?? string.Empty,
                PackageSha256 = packageSha256 ?? string.Empty
            },
            baseDirectory);

    public static bool TrySignalShellLaunchFailure(
        string machineProfile,
        IReadOnlyList<string> activeModuleIds,
        string message,
        string? baseDirectory = null,
        string? clientCode = null,
        string? moduleId = null,
        string? pluginVersion = null,
        string? packageSha256 = null)
        => TrySignalShellLaunchOutcome(
            new EdgeClientShellLaunchOutcome(
                ShellLaunchOutcomeSchemaVersion,
                EdgeClientShellLaunchStatuses.Failed,
                machineProfile,
                activeModuleIds,
                message)
            {
                ProcessId = Environment.ProcessId,
                ClientCode = clientCode ?? string.Empty,
                ModuleId = moduleId ?? string.Empty,
                PluginVersion = pluginVersion ?? string.Empty,
                PackageSha256 = packageSha256 ?? string.Empty
            },
            baseDirectory);

    public static bool TryWriteShellLaunchOutcomeToPath(
        string candidate,
        EdgeClientShellLaunchOutcome outcome,
        string? baseDirectory = null)
    {
        if (!TryValidateShellLaunchReadyPath(
                candidate,
                baseDirectory,
                out var readyPath)
            || !TryNormalizeShellLaunchOutcome(outcome, out var normalized))
        {
            return false;
        }

        var tempPath = $"{readyPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                normalized,
                ShellLaunchJsonOptions);
            if (payload.Length == 0
                || payload.Length > MaximumShellLaunchOutcomeBytes)
            {
                return false;
            }

            using (var signal = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                signal.Write(payload);
                signal.Flush(flushToDisk: true);
            }

            File.Move(tempPath, readyPath, overwrite: false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static bool TryReadShellLaunchOutcome(
        string path,
        out EdgeClientShellLaunchOutcome outcome)
    {
        outcome = default!;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists
                || file.Length <= 0
                || file.Length > MaximumShellLaunchOutcomeBytes)
            {
                return false;
            }

            var candidate = JsonSerializer.Deserialize<EdgeClientShellLaunchOutcome>(
                File.ReadAllBytes(path),
                ShellLaunchJsonOptions);
            return TryNormalizeShellLaunchOutcome(candidate, out outcome);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TrySignalShellLaunchOutcome(
        EdgeClientShellLaunchOutcome outcome,
        string? baseDirectory)
    {
        var candidate = Environment.GetEnvironmentVariable(
            ShellLaunchReadyEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(candidate)
               && TryWriteShellLaunchOutcomeToPath(
                   candidate,
                   outcome,
                   baseDirectory);
    }

    public static bool TryValidateShellLaunchReadyPath(
        string? candidate,
        string? baseDirectory,
        out string readyPath)
    {
        readyPath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)
            || candidate.Contains('\0'))
        {
            return false;
        }

        try
        {
            var launcherDirectory = Path.GetFullPath(
                EdgeClientProgramDataPaths.ResolveLauncherDirectory(baseDirectory));
            var resolved = Path.GetFullPath(candidate);
            var parent = Path.GetDirectoryName(resolved);
            var fileName = Path.GetFileName(resolved);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(parent, launcherDirectory, comparison)
                || !fileName.StartsWith(
                    ShellLaunchReadyFilePrefix,
                    StringComparison.Ordinal)
                || !fileName.EndsWith(
                    ShellLaunchReadyFileSuffix,
                    StringComparison.Ordinal)
                || fileName.Length
                != ShellLaunchReadyFilePrefix.Length
                   + 32
                   + ShellLaunchReadyFileSuffix.Length)
            {
                return false;
            }

            readyPath = resolved;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException
                                       or NotSupportedException
                                       or PathTooLongException)
        {
            return false;
        }
    }

    private static IDisposable? TryOpenShellPresence(
        string? baseDirectory,
        FileAccess access,
        FileShare share)
    {
        try
        {
            var path = ResolveShellPresenceLockPath(baseDirectory);
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException(
                    "Shell presence lock 缺少目录。"));
            return new FileStream(
                path,
                FileMode.OpenOrCreate,
                access,
                share,
                bufferSize: 1,
                FileOptions.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryNormalizeShellLaunchOutcome(
        EdgeClientShellLaunchOutcome? candidate,
        out EdgeClientShellLaunchOutcome outcome)
    {
        outcome = default!;
        if (candidate is null
            || candidate.SchemaVersion != ShellLaunchOutcomeSchemaVersion
            || candidate.ProcessId <= 0
            || string.IsNullOrWhiteSpace(candidate.MachineProfile)
            || candidate.MachineProfile.Length > 128
            || candidate.MachineProfile.Any(char.IsControl)
            || candidate.ActiveModuleIds is null
            || candidate.ActiveModuleIds.Count > 128)
        {
            return false;
        }

        var candidateStatus = candidate.Status?.Trim();
        var status = candidateStatus switch
        {
            var value when string.Equals(
                value,
                EdgeClientShellLaunchStatuses.Ready,
                StringComparison.OrdinalIgnoreCase)
                => EdgeClientShellLaunchStatuses.Ready,
            var value when string.Equals(
                value,
                EdgeClientShellLaunchStatuses.ReadyWithDiagnostics,
                StringComparison.OrdinalIgnoreCase)
                => EdgeClientShellLaunchStatuses.ReadyWithDiagnostics,
            var value when string.Equals(
                value,
                EdgeClientShellLaunchStatuses.Failed,
                StringComparison.OrdinalIgnoreCase)
                => EdgeClientShellLaunchStatuses.Failed,
            _ => null
        };
        if (status is null)
        {
            return false;
        }

        var activeModuleIds = candidate.ActiveModuleIds
            .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Select(static moduleId => moduleId.Trim())
            .Where(static moduleId =>
                moduleId.Length <= 256
                && !moduleId.Any(char.IsControl))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static moduleId => moduleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (activeModuleIds.Length != candidate.ActiveModuleIds.Count)
        {
            return false;
        }

        if (candidate.Diagnostics is null
            || candidate.Diagnostics.Count > 128)
        {
            return false;
        }

        var diagnostics = candidate.Diagnostics
            .Where(static diagnostic => diagnostic is not null)
            .Select(static diagnostic => new EdgeClientShellLaunchDiagnostic(
                diagnostic.ReasonCode?.Trim() ?? string.Empty,
                diagnostic.RepairTarget?.Trim() ?? string.Empty,
                string.IsNullOrWhiteSpace(diagnostic.ModuleId)
                    ? null
                    : diagnostic.ModuleId.Trim()))
            .Where(static diagnostic =>
                diagnostic.ReasonCode.Length is > 0 and <= 128
                && !diagnostic.ReasonCode.Any(char.IsControl)
                && diagnostic.RepairTarget.Length is > 0 and <= 128
                && !diagnostic.RepairTarget.Any(char.IsControl)
                && (diagnostic.ModuleId is null
                    || (diagnostic.ModuleId.Length <= 256
                        && !diagnostic.ModuleId.Any(char.IsControl))))
            .Distinct()
            .OrderBy(static diagnostic => diagnostic.ReasonCode, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (diagnostics.Length != candidate.Diagnostics.Count)
        {
            return false;
        }

        var message = candidate.Message?.Trim();
        if (status == EdgeClientShellLaunchStatuses.Failed
            && (string.IsNullOrWhiteSpace(message)
                || message.Length > 1024
                || message.Any(char.IsControl)))
        {
            return false;
        }
        if ((status == EdgeClientShellLaunchStatuses.Ready
             || status == EdgeClientShellLaunchStatuses.ReadyWithDiagnostics)
            && !string.IsNullOrWhiteSpace(message))
        {
            return false;
        }
        if (status == EdgeClientShellLaunchStatuses.Ready
            && diagnostics.Length != 0)
        {
            return false;
        }
        if (status == EdgeClientShellLaunchStatuses.ReadyWithDiagnostics
            && diagnostics.Length == 0)
        {
            return false;
        }
        if (status == EdgeClientShellLaunchStatuses.Failed
            && diagnostics.Length != 0)
        {
            return false;
        }

        var clientCode = candidate.ClientCode?.Trim() ?? string.Empty;
        var moduleId = candidate.ModuleId?.Trim() ?? string.Empty;
        var pluginVersion = candidate.PluginVersion?.Trim() ?? string.Empty;
        var packageSha256 = candidate.PackageSha256?.Trim().ToUpperInvariant() ?? string.Empty;
        var hasAnyDeviceFact = clientCode.Length > 0
                               || moduleId.Length > 0
                               || pluginVersion.Length > 0
                               || packageSha256.Length > 0;
        if (hasAnyDeviceFact)
        {
            try
            {
                clientCode = EdgeClientIdentity.NormalizeClientCode(clientCode);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (moduleId.Length == 0
                || moduleId.Length > 256
                || moduleId.Any(char.IsControl)
                || pluginVersion.Length == 0
                || pluginVersion.Length > 128
                || pluginVersion.Any(char.IsControl)
                || packageSha256.Length != 64
                || packageSha256.Any(static character => !Uri.IsHexDigit(character)))
            {
                return false;
            }
        }

        outcome = new EdgeClientShellLaunchOutcome(
            ShellLaunchOutcomeSchemaVersion,
            status,
            candidate.MachineProfile.Trim(),
            activeModuleIds,
            status == EdgeClientShellLaunchStatuses.Failed
                ? message
                : null)
        {
            ProcessId = candidate.ProcessId,
            Diagnostics = diagnostics,
            ClientCode = clientCode,
            ModuleId = moduleId,
            PluginVersion = pluginVersion,
            PackageSha256 = packageSha256
        };
        return true;
    }
}
