using System.Threading;

namespace IIoT.Edge.Shell.Core;

internal enum SingleInstanceMutexAcquireResult
{
    Acquired,
    AlreadyOwned,
    Unavailable
}

internal sealed class SingleInstanceMutexHandle : IDisposable
{
    private Mutex? _mutex;

    public bool OwnsMutex { get; private set; }

    public bool TryAcquire(string mutexName)
        => TryAcquireNonBlocking(mutexName, out _) == SingleInstanceMutexAcquireResult.Acquired;

    public SingleInstanceMutexAcquireResult TryAcquireNonBlocking(
        string mutexName,
        out Exception? failure)
    {
        Release();

        Mutex? mutex = null;
        try
        {
            if (string.IsNullOrWhiteSpace(mutexName)
                || mutexName.Contains('\0')
                || mutexName.Length > 256)
            {
                throw new ArgumentException("命名互斥量名称无效。", nameof(mutexName));
            }

            mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                failure = null;
                return SingleInstanceMutexAcquireResult.AlreadyOwned;
            }

            _mutex = mutex;
            OwnsMutex = true;
            failure = null;
            return SingleInstanceMutexAcquireResult.Acquired;
        }
        catch (Exception ex)
        {
            mutex?.Dispose();
            failure = ex;
            return SingleInstanceMutexAcquireResult.Unavailable;
        }
    }

    public void Release()
    {
        var mutex = _mutex;
        var ownsMutex = OwnsMutex;

        _mutex = null;
        OwnsMutex = false;

        if (mutex is null)
        {
            return;
        }

        try
        {
            if (ownsMutex)
            {
                mutex.ReleaseMutex();
            }
        }
        finally
        {
            mutex.Dispose();
        }
    }

    public void Dispose() => Release();
}
