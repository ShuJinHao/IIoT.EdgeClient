using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using System.IO;
using System.Windows;

namespace IIoT.Edge.Launcher;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            EnsureLauncherAccountsFileExists(baseDirectory);
            var accountCatalog = new LauncherAccountCatalog(baseDirectory);
            var profileCatalog = new LauncherProfileCatalog(baseDirectory);
            var authService = new LocalLauncherAuthService(accountCatalog);
            var launchService = new ShellLaunchService(new ProcessStarter());
            var viewModel = new LauncherMainViewModel(profileCatalog, authService, launchService);
            var mainWindow = new MainWindow(viewModel);
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

    private void EnsureLauncherAccountsFileExists(string baseDirectory)
    {
        var accountsPath = LauncherAccountCatalog.GetCatalogPath(baseDirectory);
        if (File.Exists(accountsPath))
        {
            return;
        }

        var samplePath = LauncherAccountCatalog.GetCatalogPath(
            baseDirectory,
            LauncherAccountCatalog.SampleCatalogFileName);
        if (!File.Exists(samplePath))
        {
            throw new FileNotFoundException(
                $"启动账号文件不存在，且未找到样例文件：{samplePath}",
                samplePath);
        }

        File.Copy(samplePath, accountsPath, overwrite: false);
    }
}
