using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.AvaloniaShell.ViewModels;
using IIoT.Edge.Host.Bootstrap.Avalonia;
using IIoT.Edge.Presentation.Navigation.Avalonia;
using IIoT.Edge.Presentation.Shell.Avalonia.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Modularity;
using IIoT.Edge.UI.Avalonia.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class AvaloniaShellBehaviorTests
{
    [AvaloniaFact]
    public void Bootstrap_registers_real_shell_menu_and_resource_contributors()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");
        var registry = provider.GetRequiredService<IAvaloniaViewRegistry>();
        var menus = registry.GetAllMenus();
        var ids = StandardAvaloniaModuleViewIds.Create("Homogenization");

        Assert.Contains(menus, item => item.ViewId == ids.Monitor);
        Assert.Contains(menus, item => item.ViewId == ids.DataView);
        Assert.Contains(menus, item => item.ViewId == ids.CapacityView);
        Assert.Contains(menus, item => item.ViewId == ids.PlcTaskBindingView);
        Assert.Contains(menus, item => item.ViewId == CoreAvaloniaViewIds.Diagnostics);
        Assert.Equal("监控", provider.GetRequiredService<IAvaloniaLanguageService>().GetText("Navigation_Menu_Monitor"));
    }

    [AvaloniaFact]
    public void Main_window_view_model_builds_dock_layout_from_registry()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");

        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        var ids = StandardAvaloniaModuleViewIds.Create("Homogenization");

        Assert.NotNull(viewModel.DockLayout);
        Assert.True(viewModel.MenuItems.Count >= 5);
        Assert.Contains(viewModel.MenuItems, item => item.ViewId == ids.Monitor && item.Title == "监控");

        viewModel.ToggleLanguageCommand.Execute(null);

        Assert.Equal("en-US", viewModel.CultureName);
        Assert.Contains(viewModel.MenuItems, item => item.ViewId == ids.Monitor && item.Title == "Monitor");
    }

    [AvaloniaFact]
    public void Navigation_service_creates_first_batch_pages()
    {
        using var provider = BuildProvider();
        var navigation = provider.GetRequiredService<IAvaloniaNavigationService>();
        var ids = StandardAvaloniaModuleViewIds.Create("Homogenization");

        foreach (var viewId in new[]
                 {
                     ids.Monitor,
                     ids.DataView,
                     ids.CapacityView,
                     ids.PlcTaskBindingView,
                     CoreAvaloniaViewIds.Diagnostics
                 })
        {
            navigation.NavigateTo(viewId);

            Assert.NotNull(navigation.CurrentView);
            Assert.NotNull(navigation.CurrentViewModel);
        }
    }

    [AvaloniaFact]
    public void Localized_datagrid_refreshes_column_header_from_resource()
    {
        global::Avalonia.Application.Current!.Resources["Test_Header"] = "测试列";
        var column = new DataGridTextColumn();

        LocalizedDataGrid.SetHeaderResourceKey(column, "Test_Header");
        LocalizedDataGrid.RefreshHeaders();

        Assert.Equal("测试列", column.Header);
    }

    [AvaloniaFact]
    public async Task Dialog_dispatcher_timer_and_window_services_are_available()
    {
        using var provider = BuildProvider();
        var dialogService = provider.GetRequiredService<IAvaloniaDialogService>();
        AvaloniaDialogRequest? request = null;
        dialogService.DialogRequested += (_, value) => request = value;

        await dialogService.ShowInfoAsync("标题", "内容");

        Assert.Equal("标题", request?.Title);
        Assert.Equal("内容", request?.Message);

        var dispatcher = provider.GetRequiredService<IAvaloniaDispatcherService>();
        var invoked = false;
        await dispatcher.InvokeAsync(() => invoked = true);
        Assert.True(invoked);

        Assert.NotNull(provider.GetRequiredService<IAvaloniaTimerFactory>().Create(TimeSpan.FromSeconds(1)));
        Assert.NotNull(provider.GetRequiredService<IAvaloniaWindowService>());
        Assert.NotNull(provider.GetRequiredService<HeaderViewModel>());
        Assert.NotNull(provider.GetRequiredService<FooterViewModel>());
        Assert.NotNull(provider.GetRequiredService<LoginViewModel>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection()
            .AddEdgeHostAvaloniaBootstrap(CreateOptions())
            .AddSingleton<MainWindowViewModel>()
            .BuildServiceProvider();

        IIoT.Edge.Host.Bootstrap.Avalonia.DependencyInjection.RegisterAvaloniaViews(services);
        return services;
    }

    private static AvaloniaHostBootstrapOptions CreateOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "iiot-edge-avalonia-tests", Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shell:Environment"] = "AvaloniaShellTests",
                ["LocalAdmin:PasswordHash"] = string.Empty,
                ["CloudApi:BaseUrl"] = "http://127.0.0.1",
                ["MesApi:BaseUrl"] = "http://127.0.0.1"
            })
            .Build();

        var runtimePaths = new EdgeRuntimePaths(
            BaseDirectory: root,
            ProfileName: "AvaloniaShellTests",
            RuntimeDataRoot: root,
            DatabaseDirectory: Path.Combine(root, "db"),
            ContextDirectory: Path.Combine(root, "context"),
            RecipeDirectory: Path.Combine(root, "recipe"),
            ExcelDirectory: Path.Combine(root, "excel"),
            DiagnosticsDirectory: Path.Combine(root, "diagnostics"),
            LogDirectory: Path.Combine(root, "diagnostics", "logs"),
            DeviceCacheFilePath: Path.Combine(root, "device_cache.json"),
            PrimaryCrashLogPath: Path.Combine(root, "diagnostics", "crash.log"),
            FallbackCrashLogPath: Path.Combine(root, "diagnostics", "crash.fallback.log"));

        return new AvaloniaHostBootstrapOptions(
            configuration,
            runtimePaths,
            "AvaloniaShellTests",
            ["Homogenization"]);
    }
}
