using Avalonia;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Infrastructure.Update.Startup;

namespace IIoT.Edge.Launcher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        EdgeUpdateVelopackStartup.Run();

        try
        {
            using var machineLock = LauncherMachineLock.TryAcquire();
            if (machineLock is null)
            {
                Environment.ExitCode = 2;
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (LauncherMachineLockException)
        {
            Environment.ExitCode = 3;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
