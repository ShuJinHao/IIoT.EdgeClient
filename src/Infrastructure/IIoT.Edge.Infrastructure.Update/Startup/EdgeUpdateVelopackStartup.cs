using Velopack;

namespace IIoT.Edge.Infrastructure.Update.Startup;

public static class EdgeUpdateVelopackStartup
{
    public static void Run()
    {
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .Run();
    }
}
