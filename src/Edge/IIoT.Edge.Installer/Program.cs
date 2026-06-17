using Avalonia;

namespace IIoT.Edge.Installer;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        InstallerOptions options;
        try
        {
            options = InstallerOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 64;
        }

        if (options.Silent)
        {
            return InstallerService.RunSilent(options);
        }

        App.Options = options;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
