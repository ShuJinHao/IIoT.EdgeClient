using System.Threading;

namespace IIoT.Edge.Launcher.Services;

public sealed class NamedMutexShellInstanceProbe : IShellInstanceProbe
{
    public bool IsInstanceRunning(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return false;
        }

        var mutexName = $"Global\\IIoT.EdgeClient_{instanceId.Trim()}";
        try
        {
            using var _ = Mutex.OpenExisting(mutexName);
            return true;
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
