using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherUpdateOperationGate
{
    IDisposable? TryAcquire();

    IDisposable? TryAcquireUpdate();

    string CreateShellLaunchReadyPath();
}

public sealed class FileLauncherUpdateOperationGate
    : ILauncherUpdateOperationGate
{
    public const string LockFileName =
        EdgeClientUpdateCoordination.UpdateOperationLockFileName;

    private readonly string _baseDirectory;
    private readonly string _lockPath;

    public FileLauncherUpdateOperationGate(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _lockPath = EdgeClientUpdateCoordination.ResolveUpdateOperationLockPath(
            baseDirectory);
    }

    public IDisposable? TryAcquire()
        => TryAcquireExclusive(_lockPath);

    public IDisposable? TryAcquireUpdate()
    {
        var operationLease = TryAcquire();
        if (operationLease is null)
        {
            return null;
        }

        var shellPresenceLease =
            EdgeClientUpdateCoordination.TryAcquireExclusiveShellPresence(
                _baseDirectory);
        if (shellPresenceLease is null)
        {
            operationLease.Dispose();
            return null;
        }

        return new CompositeLease(shellPresenceLease, operationLease);
    }

    public string CreateShellLaunchReadyPath()
    {
        var path = EdgeClientUpdateCoordination.CreateShellLaunchReadyPath(
            _baseDirectory);
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Shell 启动握手文件缺少目录。"));
        return path;
    }

    private static IDisposable? TryAcquireExclusive(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("更新门控文件缺少目录。");
            Directory.CreateDirectory(directory);
            return new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
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

    private sealed class CompositeLease(
        IDisposable shellPresenceLease,
        IDisposable operationLease) : IDisposable
    {
        private IDisposable? _shellPresenceLease = shellPresenceLease;
        private IDisposable? _operationLease = operationLease;

        public void Dispose()
        {
            Interlocked.Exchange(ref _shellPresenceLease, null)?.Dispose();
            Interlocked.Exchange(ref _operationLease, null)?.Dispose();
        }
    }
}
