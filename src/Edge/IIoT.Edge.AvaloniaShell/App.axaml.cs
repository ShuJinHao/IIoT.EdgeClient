using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using IIoT.Edge.AvaloniaShell.ViewModels;
using IIoT.Edge.AvaloniaShell.Views;
using IIoT.Edge.UI.Avalonia.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.AvaloniaShell;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection()
                .AddAvaloniaShell()
                .BuildServiceProvider();
            ShellAvaloniaRegistration.RegisterShellViews(services);

            var languageService = services.GetRequiredService<IAvaloniaLanguageService>();
            languageService.Apply("zh-CN");

            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
