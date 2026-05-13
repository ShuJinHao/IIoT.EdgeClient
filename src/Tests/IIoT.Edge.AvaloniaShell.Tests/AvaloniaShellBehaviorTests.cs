using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IIoT.Edge.AvaloniaShell.Localization;
using IIoT.Edge.AvaloniaShell.ViewModels;
using IIoT.Edge.UI.Avalonia;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Modularity;
using IIoT.Edge.UI.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class AvaloniaShellBehaviorTests
{
    [AvaloniaFact]
    public void Language_service_updates_application_resources_and_toggle_label()
    {
        var service = new AvaloniaResourceLanguageService(ShellLanguageResources.Create());

        service.Apply("zh-CN");

        Assert.Equal("生产监控", service.GetText("Shell_Tab_Monitor"));
        Assert.Equal("EN", service.ToggleLabel);
        object? zhValue = null;
        var found = global::Avalonia.Application.Current?.TryFindResource("Shell_Tab_Monitor", out zhValue) == true;
        Assert.True(found);
        Assert.Equal("生产监控", zhValue);

        service.Toggle();

        Assert.Equal("Monitor", service.GetText("Shell_Tab_Monitor"));
        Assert.Equal("中", service.ToggleLabel);
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
    public async Task Dialog_and_dispatcher_services_raise_expected_actions()
    {
        var dialogService = new AvaloniaDialogService();
        AvaloniaDialogRequest? request = null;
        dialogService.DialogRequested += (_, value) => request = value;

        await dialogService.ShowInfoAsync("标题", "内容");

        Assert.Equal("标题", request?.Title);
        Assert.Equal("内容", request?.Message);

        var dispatcher = new AvaloniaDispatcherService();
        var invoked = false;
        await dispatcher.InvokeAsync(() => invoked = true);

        Assert.True(invoked);
    }

    [AvaloniaFact]
    public void Navigation_registry_creates_registered_view_and_view_model()
    {
        var services = new ServiceCollection();
        services.AddAvaloniaUiShared();
        services.AddSingleton<TestViewModel>();
        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAvaloniaViewRegistry>();
        registry.RegisterRoute("test", typeof(TestView), typeof(TestViewModel), sp => sp.GetRequiredService<TestViewModel>());
        var navigation = provider.GetRequiredService<IAvaloniaNavigationService>();

        navigation.NavigateTo("test");

        Assert.IsType<TestView>(navigation.CurrentView);
        Assert.IsType<TestViewModel>(navigation.CurrentViewModel);
    }

    [AvaloniaFact]
    public void Shell_registration_builds_dock_layout_and_menu_items()
    {
        var services = new ServiceCollection()
            .AddAvaloniaShell()
            .BuildServiceProvider();
        ShellAvaloniaRegistration.RegisterShellViews(services);
        services.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");

        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        Assert.NotNull(viewModel.DockLayout);
        Assert.Equal(2, viewModel.MenuItems.Count);
        Assert.Contains(viewModel.MenuItems, item => item.ViewId == "monitor" && item.Title == "生产监控");

        viewModel.ToggleLanguageCommand.Execute(null);

        Assert.Equal("en-US", viewModel.CultureName);
        Assert.Contains(viewModel.MenuItems, item => item.ViewId == "monitor" && item.Title == "Monitor");
    }

    private sealed class TestView : UserControl
    {
    }

    private sealed class TestViewModel
    {
    }
}
