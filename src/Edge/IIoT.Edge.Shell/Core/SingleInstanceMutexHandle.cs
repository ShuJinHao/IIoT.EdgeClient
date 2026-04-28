using System.Threading;

namespace IIoT.Edge.Shell.Core;

internal sealed class SingleInstanceMutexHandle : IDisposable
{
    private Mutex? _mutex;

    public bool OwnsMutex { get; private set; }

    public bool TryAcquire(string mutexName)
    {
        Release();

        var mutex = new Mutex(true, mutexName, out var createdNew);
        if (createdNew)
        {
            _mutex = mutex;
            OwnsMutex = true;
            return true;
        }

        mutex.Dispose();
        return false;
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
