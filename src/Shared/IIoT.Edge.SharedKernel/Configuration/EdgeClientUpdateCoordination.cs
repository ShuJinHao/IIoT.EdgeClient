using System.Text.Json;

namespace IIoT.Edge.SharedKernel.Configuration;

public static class EdgeClientShellLaunchStatuses
{
    public const string Ready = "ready";
    public const string Failed = "failed";
}

public sealed record EdgeClientShellLaunchOutcome(
    int SchemaVersion,
    string Status,
    string MachineProfile,
    IReadOnlyList<string> ActiveModuleIds,
    string? Message);

public static class EdgeClientUpdateCoordination
{
    public const int ShellLaunchOutcomeSchemaVersion = 1;
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
        string? baseDirectory = null)
        => TrySignalShellLaunchOutcome(
            new EdgeClientShellLaunchOutcome(
                ShellLaunchOutcomeSchemaVersion,
                EdgeClientShellLaunchStatuses.Ready,
                machineProfile,
                activeModuleIds,
                Message: null),
            baseDirectory);

    public static bool TrySignalShellLaunchFailure(
        string machineProfile,
        IReadOnlyList<string> activeModuleIds,
        string message,
        string? baseDirectory = null)
        => TrySignalShellLaunchOutcome(
            new EdgeClientShellLaunchOutcome(
                ShellLaunchOutcomeSchemaVersion,
                EdgeClientShellLaunchStatuses.Failed,
                machineProfile,
                activeModuleIds,
                message),
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
            || string.IsNullOrWhiteSpace(candidate.MachineProfile)
            || candidate.MachineProfile.Length > 128
            || candidate.MachineProfile.Any(char.IsControl)
            || candidate.ActiveModuleIds is null
            || candidate.ActiveModuleIds.Count > 128)
        {
            return false;
        }

        var status = candidate.Status?.Trim().ToLowerInvariant();
        if (status is not (
            EdgeClientShellLaunchStatuses.Ready
            or EdgeClientShellLaunchStatuses.Failed))
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

        var message = candidate.Message?.Trim();
        if (status == EdgeClientShellLaunchStatuses.Failed
            && (string.IsNullOrWhiteSpace(message)
                || message.Length > 1024
                || message.Any(char.IsControl)))
        {
            return false;
        }
        if (status == EdgeClientShellLaunchStatuses.Ready
            && !string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        outcome = new EdgeClientShellLaunchOutcome(
            ShellLaunchOutcomeSchemaVersion,
            status,
            candidate.MachineProfile.Trim(),
            activeModuleIds,
            status == EdgeClientShellLaunchStatuses.Failed
                ? message
                : null);
        return true;
    }
}
