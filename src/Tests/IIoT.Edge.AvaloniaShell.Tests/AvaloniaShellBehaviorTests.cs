using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.AvaloniaShell.ViewModels;
using IIoT.Edge.Host.Bootstrap.Avalonia;
using IIoT.Edge.Presentation.Navigation.Avalonia;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.HardwareConfig.ViewModels;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;
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
        Assert.Contains(menus, item => item.ViewId == ids.HardwareConfigView);
        Assert.Contains(menus, item => item.ViewId == ids.IoView);
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
        Assert.True(viewModel.MenuItems.Count >= 7);
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
                     ids.HardwareConfigView,
                     ids.IoView,
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
        global::Avalonia.Application.Current!.Resources["Test_Header"] = "Initial";
        var column = new DataGridTextColumn();

        LocalizedDataGrid.SetHeaderResourceKey(column, "Test_Header");
        LocalizedDataGrid.RefreshHeaders();

        Assert.Equal("Initial", column.Header);

        global::Avalonia.Application.Current!.Resources["Test_Header"] = "Updated";
        LocalizedDataGrid.RefreshHeaders();

        Assert.Equal("Updated", column.Header);
    }

    [AvaloniaFact]
    public void Hardware_config_page_uses_fake_data_and_confirmation_flow()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");
        var navigation = provider.GetRequiredService<IAvaloniaNavigationService>();
        var ids = StandardAvaloniaModuleViewIds.Create("Homogenization");

        navigation.NavigateTo(ids.HardwareConfigView);

        var viewModel = Assert.IsType<HardwareConfigViewModel>(navigation.CurrentViewModel);
        var originalNetworkCount = viewModel.NetworkDevices.Count;
        viewModel.AddNetworkDeviceCommand.Execute(null);
        Assert.Equal(originalNetworkCount + 1, viewModel.NetworkDevices.Count);

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);
        Assert.True(viewModel.IsDialogOpen);
        viewModel.ConfirmDialogCommand.Execute(null);
        Assert.False(viewModel.IsDialogOpen);
        Assert.Contains(viewModel.FilteredIoMappings, item => item.SignalName == "新信号" && item.BusinessGroup == "数据点");

        viewModel.SaveCommand.Execute(null);
        Assert.True(viewModel.IsDialogOpen);
        Assert.Equal("保存硬件配置", viewModel.DialogTitle);
        Assert.Equal("保存确认待执行", viewModel.PendingOperationText);
        viewModel.ConfirmDialogCommand.Execute(null);
        Assert.False(viewModel.IsDialogOpen);
    }

    [AvaloniaFact]
    public async Task Io_view_uses_fake_services_without_accessing_real_plc()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");
        var navigation = provider.GetRequiredService<IAvaloniaNavigationService>();
        var ids = StandardAvaloniaModuleViewIds.Create("Homogenization");

        navigation.NavigateTo(ids.IoView);

        var viewModel = Assert.IsType<IoViewViewModel>(navigation.CurrentViewModel);
        Assert.NotNull(viewModel.SelectedDevice);
        Assert.True(viewModel.HasInteractionRows);
        Assert.True(viewModel.HasDataSections);

        await viewModel.ManualReadCommand.ExecuteAsync(null);
        Assert.Contains("未连接真实 PLC", viewModel.FeedbackMessage);

        var row = viewModel.InteractionRows.First();
        Assert.NotNull(row.WriteCommand);
        await row.WriteCommand.ExecuteAsync(null);
        Assert.Contains("未连接真实 PLC", viewModel.FeedbackMessage);
    }

    [AvaloniaFact]
    public async Task Dialog_service_raises_info_request_and_completes_confirm_request()
    {
        using var provider = BuildProvider();
        var dialogService = provider.GetRequiredService<IAvaloniaDialogService>();
        var requests = new List<AvaloniaDialogRequest>();
        dialogService.DialogRequested += (_, value) =>
        {
            requests.Add(value);
            if (value.Kind == AvaloniaDialogRequestKind.Confirm)
            {
                value.Complete(true);
            }
        };

        await dialogService.ShowInfoAsync("Info", "Message");
        var confirmed = await dialogService.ConfirmAsync("Confirm", "Continue?");

        Assert.Equal(2, requests.Count);
        Assert.Equal(AvaloniaDialogRequestKind.Info, requests[0].Kind);
        Assert.Equal("Info", requests[0].Title);
        Assert.Equal("Message", requests[0].Message);
        Assert.True(requests[0].IsCompleted);
        Assert.Equal(AvaloniaDialogRequestKind.Confirm, requests[1].Kind);
        Assert.Equal("Confirm", requests[1].Title);
        Assert.Equal("Continue?", requests[1].Message);
        Assert.True(requests[1].IsCompleted);
        Assert.True(confirmed);
    }

    [AvaloniaFact]
    public async Task Confirm_dialog_defaults_to_false_when_no_host_handles_request()
    {
        using var provider = BuildProvider();
        var dialogService = provider.GetRequiredService<IAvaloniaDialogService>();

        var confirmed = await dialogService.ConfirmAsync("Confirm", "Continue?");

        Assert.False(confirmed);
    }

    [AvaloniaFact]
    public async Task Dispatcher_timer_and_window_services_are_available()
    {
        using var provider = BuildProvider();
        var dispatcher = provider.GetRequiredService<IAvaloniaDispatcherService>();
        var invoked = false;
        await dispatcher.InvokeAsync(() => invoked = true);
        Assert.True(invoked);

        var timer = provider.GetRequiredService<IAvaloniaTimerFactory>().Create(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), timer.Interval);
        Assert.False(timer.IsEnabled);
        timer.Start();
        Assert.True(timer.IsEnabled);
        timer.Stop();
        Assert.False(timer.IsEnabled);

        var windowService = provider.GetRequiredService<IAvaloniaWindowService>();
        Assert.Equal("WindowMaximize", windowService.MaxRestoreIcon);
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
