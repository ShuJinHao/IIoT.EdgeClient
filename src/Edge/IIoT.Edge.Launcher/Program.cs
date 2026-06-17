using Avalonia;
using IIoT.Edge.Infrastructure.Update.Startup;

namespace IIoT.Edge.Launcher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        EdgeUpdateVelopackStartup.Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
