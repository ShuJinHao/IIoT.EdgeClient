using System.Security;

namespace IIoT.Edge.Launcher.Services;

internal sealed class LauncherMachineLockException(
    string reasonCode,
    Exception innerException) : InvalidOperationException(reasonCode, innerException)
{
    public string ReasonCode { get; } = reasonCode;
}

/// <summary>
/// One Launcher owns the whole machine. The lease is acquired after the Velopack lifecycle hook
/// and before Avalonia, DI, SQLite or shared-file initialization.
/// </summary>
internal sealed class LauncherMachineLock : IDisposable
{
    public const string GlobalMutexName = @"Global\IIoT.Edge.Launcher";

    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private LauncherMachineLock(Mutex mutex)
    {
        _mutex = mutex;
        _ownsMutex = true;
    }

    public static LauncherMachineLock? TryAcquire(string mutexName = GlobalMutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, mutexName);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                             or SecurityException
                                             or IOException
                                             or PlatformNotSupportedException)
        {
            throw new LauncherMachineLockException(
                "LAUNCHER_MACHINE_LOCK_CREATE_FAILED",
                exception);
        }

        try
        {
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // Ownership is granted by WaitOne; startup recovery must run before any other work.
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }

            return new LauncherMachineLock(mutex);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                             or SecurityException
                                             or IOException
                                             or ApplicationException)
        {
            mutex.Dispose();
            throw new LauncherMachineLockException(
                "LAUNCHER_MACHINE_LOCK_ACQUIRE_FAILED",
                exception);
        }
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _ownsMutex = false;
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
