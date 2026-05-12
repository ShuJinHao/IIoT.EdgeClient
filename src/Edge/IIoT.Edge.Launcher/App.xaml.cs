using IIoT.Edge.Launcher.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace IIoT.Edge.Launcher;

public partial class App : System.Windows.Application
{
    private IServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _serviceProvider = ConfigureServices(AppDomain.CurrentDomain.BaseDirectory)
                .BuildServiceProvider();
            _serviceProvider.GetRequiredService<ILauncherAccountCatalogInitializer>()
                .EnsureCatalogExists();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"本地启动器初始化失败：{ex.Message}",
                "IIoT Edge Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _serviceProvider = null;
        base.OnExit(e);
    }

    private static IServiceCollection ConfigureServices(string baseDirectory)
        => new ServiceCollection().AddLauncherServices(baseDirectory);
}
