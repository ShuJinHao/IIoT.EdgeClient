using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using IIoT.Edge.AvaloniaPoc.Services;
using IIoT.Edge.AvaloniaPoc.ViewModels;
using IIoT.Edge.AvaloniaPoc.Views;

namespace IIoT.Edge.AvaloniaPoc;

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
            var languageService = new AvaloniaAppLanguageService();
            languageService.Apply("zh-CN");

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(languageService)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
