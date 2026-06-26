namespace IIoT.Edge.Launcher.Services;

public interface IShellInstanceProbe
{
    bool IsInstanceRunning(string instanceId);
}
