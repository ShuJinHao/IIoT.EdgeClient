using System.Diagnostics;

namespace IIoT.Edge.Launcher.Services;

public interface IProcessStarter
{
    Process? Start(ProcessStartInfo startInfo);
}
