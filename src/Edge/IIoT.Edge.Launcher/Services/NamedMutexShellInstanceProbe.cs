using System.Threading;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

public sealed class NamedMutexShellInstanceProbe : IShellInstanceProbe
{
    public bool IsInstanceRunning(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        var mutexName = EdgeClientInstanceMutexName.Create(instanceId);
        try
        {
            using var mutex = Mutex.OpenExisting(mutexName);
            try
            {
                if (!mutex.WaitOne(0))
                {
                    return true;
                }

                mutex.ReleaseMutex();
                return false;
            }
            catch (AbandonedMutexException)
            {
                // WaitOne grants ownership when the previous process abandoned the mutex.
                // Release that ownership and treat the stale name as not running.
                mutex.ReleaseMutex();
                return false;
            }
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
