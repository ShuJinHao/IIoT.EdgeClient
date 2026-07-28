namespace IIoT.Edge.SharedKernel.Configuration;

public static class EdgeClientUpdateCoordination
{
    public const string UpdateOperationLockFileName = "update-operation.lock";
    public const string ShellPresenceLockFileName = "shell-presence.lock";
    public const string ShellLaunchReadyEnvironmentVariable =
        "IIOT_EDGE_SHELL_LAUNCH_READY_PATH";

    private const string ShellLaunchReadyFilePrefix = ".shell-launch-ready-";
    private const string ShellLaunchReadyFileSuffix = ".signal";

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

    public static bool TrySignalShellLaunchReady(string? baseDirectory = null)
    {
        var candidate = Environment.GetEnvironmentVariable(
            ShellLaunchReadyEnvironmentVariable);
        if (!TryValidateShellLaunchReadyPath(
                candidate,
                baseDirectory,
                out var readyPath))
        {
            return false;
        }

        try
        {
            using var signal = new FileStream(
                readyPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.WriteThrough);
            signal.Flush(flushToDisk: true);
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
}
