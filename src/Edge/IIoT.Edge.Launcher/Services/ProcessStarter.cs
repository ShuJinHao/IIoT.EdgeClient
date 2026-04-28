using System.Diagnostics;

namespace IIoT.Edge.Launcher.Services;

public sealed class ProcessStarter : IProcessStarter
{
    public Process? Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}
