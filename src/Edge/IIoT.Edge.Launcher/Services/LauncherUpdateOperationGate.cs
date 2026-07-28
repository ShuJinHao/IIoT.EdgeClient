using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherUpdateOperationGate
{
    IDisposable? TryAcquire();
}

public sealed class FileLauncherUpdateOperationGate
    : ILauncherUpdateOperationGate
{
    public const string LockFileName = "update-operation.lock";

    private readonly string _lockPath;

    public FileLauncherUpdateOperationGate(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _lockPath = Path.Combine(
            EdgeClientProgramDataPaths.ResolveLauncherDirectory(baseDirectory),
            LockFileName);
    }

    public IDisposable? TryAcquire()
    {
        try
        {
            var directory = Path.GetDirectoryName(_lockPath)
                ?? throw new InvalidOperationException("更新门控文件缺少目录。");
            Directory.CreateDirectory(directory);
            return new FileStream(
                _lockPath,
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
}

internal sealed class NoopLauncherUpdateOperationGate
    : ILauncherUpdateOperationGate
{
    public static NoopLauncherUpdateOperationGate Instance { get; } = new();

    public IDisposable TryAcquire() => NoopLease.Instance;

    private sealed class NoopLease : IDisposable
    {
        public static NoopLease Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
