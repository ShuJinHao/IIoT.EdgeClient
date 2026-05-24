using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using IIoT.Edge.Application.Auth.LocalAccounts;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Launcher;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += (_, _) => DisposeServices();
            StartLauncher(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartLauncher(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            _serviceProvider = new ServiceCollection()
                .AddLauncherServices(AppDomain.CurrentDomain.BaseDirectory)
                .BuildServiceProvider();
            _serviceProvider.GetRequiredService<ILocalAccountCatalogInitializer>()
                .EnsureCatalogExists();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            DisposeServices();
            ShowStartupError(desktop, $"本地启动器初始化失败：{ex.Message}");
        }
    }

    private void DisposeServices()
    {
        _serviceProvider?.Dispose();
        _serviceProvider = null;
    }

    private static void ShowStartupError(IClassicDesktopStyleApplicationLifetime desktop, string message)
    {
        var closeButton = new Button
        {
            Content = "关闭",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(18, 8)
        };

        var window = new Window
        {
            Title = "IIoT Edge Launcher",
            Width = 460,
            Height = 220,
            MinWidth = 420,
            MinHeight = 200,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.Parse("#12181C")),
            Content = new Border
            {
                Margin = new Thickness(1),
                Padding = new Thickness(24),
                Background = new SolidColorBrush(Color.Parse("#161D22")),
                BorderBrush = new SolidColorBrush(Color.Parse("#334047")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "启动失败",
                            FontSize = 20,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brushes.White
                        },
                        new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#CBD5E1"))
                        },
                        closeButton
                    }
                }
            }
        };

        closeButton.Click += (_, _) => window.Close();
        window.Closed += (_, _) => desktop.Shutdown(-1);
        desktop.MainWindow = window;
        window.Show();
    }
}
