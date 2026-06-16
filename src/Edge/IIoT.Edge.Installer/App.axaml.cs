using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace IIoT.Edge.Installer;

public partial class App : Application
{
    internal static InstallerOptions Options { get; set; } = new(null, false, false);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new InstallerWindow(Options);
            desktop.MainWindow = window;
            window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
