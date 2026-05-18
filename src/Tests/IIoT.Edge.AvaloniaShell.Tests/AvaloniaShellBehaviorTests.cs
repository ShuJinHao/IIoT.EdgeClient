using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Dock.Model.Mvvm.Controls;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Diagnostics;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Config.ParamView.Models;
using IIoT.Edge.Application.Features.Formula.RecipeView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.AvaloniaShell.Services;
using IIoT.Edge.AvaloniaShell.ViewModels;
using IIoT.Edge.AvaloniaShell.Views;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.Module.Homogenization.Presentation;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Presentation.Navigation.Avalonia;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.HardwareConfig.ViewModels;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.Presentation.Navigation.Avalonia.Views;
using IIoT.Edge.Presentation.Panels.Avalonia.Views;
using IIoT.Edge.Presentation.Shell.Avalonia.ViewModels;
using IIoT.Edge.Presentation.Shell.Avalonia.Features.SysMenu.ViewModels;
using IIoT.Edge.SharedKernel.DataPipeline.Recipe;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Modularity;
using IIoT.Edge.UI.Avalonia.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class AvaloniaShellBehaviorTests
{
    [Fact]
    public void Shell_configuration_loader_loads_environment_machine_profile_and_plugin_defaults()
    {
        var root = CreateTempDirectory();
        var oldEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
            File.WriteAllText(
                Path.Combine(root, "appsettings.json"),
                """
                {
                  "Shell": { "MachineProfile": "LineA" },
                  "Custom": { "Value": "base" }
                }
                """);
            File.WriteAllText(
                Path.Combine(root, "appsettings.Development.json"),
                """
                {
                  "Custom": { "Value": "environment" }
                }
                """);
            File.WriteAllText(
                Path.Combine(root, "appsettings.machine.LineA.json"),
                """
                {
                  "Custom": { "Machine": "loaded" }
                }
                """);
            var pluginConfig = Path.Combine(root, "Modules", "Homogenization", "Config");
            Directory.CreateDirectory(pluginConfig);
            File.WriteAllText(
                Path.Combine(pluginConfig, "defaults.module.json"),
                """
                {
                  "Plugin": { "Default": "loaded" }
                }
                """);

            var result = new ShellConfigurationLoader().Load(root);

            Assert.Equal("Development", result.EnvironmentName);
            Assert.Equal("LineA", result.MachineProfile);
            Assert.Equal("appsettings.machine.LineA.json", result.MachineProfileFileName);
            Assert.True(result.IsMachineProfileLoaded);
            Assert.Equal("environment", result.Configuration["Custom:Value"]);
            Assert.Equal("loaded", result.Configuration["Custom:Machine"]);
            Assert.Equal("loaded", result.Configuration["Plugin:Default"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", oldEnvironment);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Shell_runtime_path_resolver_uses_configured_relative_runtime_root()
    {
        var root = CreateTempDirectory();
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Shell:MachineProfile"] = "LineA",
                    ["Shell:RuntimeDataRoot"] = "runtime/line-a"
                })
                .Build();

            var paths = new ShellRuntimePathResolver().Resolve(root, configuration);

            Assert.Equal("LineA", paths.ProfileName);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "runtime", "line-a")), paths.RuntimeDataRoot);
            Assert.Equal(Path.Combine(paths.RuntimeDataRoot, "diagnostics", "logs"), paths.LogDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Avalonia_bootstrap_options_factory_uses_configuration_paths_and_modules()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "appsettings.json"),
                """
                {
                  "Shell": {
                    "RuntimeDataRoot": "runtime",
                    "MachineProfile": "LineA"
                  },
                  "Modules": {
                    "Enabled": [ "Homogenization", "CustomModule" ]
                  }
                }
                """);

            var factory = new AvaloniaShellBootstrapOptionsFactory(
                new ShellConfigurationLoader(),
                new ShellRuntimePathResolver());

            var options = factory.Create(root);

            Assert.Equal(Path.GetFullPath(Path.Combine(root, "runtime")), options.RuntimePaths.RuntimeDataRoot);
            Assert.Equal(["Homogenization", "CustomModule"], options.ModuleIds);
            Assert.Equal([root], options.PluginDirectories);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Avalonia_shell_project_contains_copied_appsettings_files()
    {
        var root = FindRepositoryRoot();
        var projectDirectory = Path.Combine(root, "src", "Edge", "IIoT.Edge.AvaloniaShell");
        var projectFile = File.ReadAllText(Path.Combine(projectDirectory, "IIoT.Edge.AvaloniaShell.csproj"));

        Assert.True(File.Exists(Path.Combine(projectDirectory, "appsettings.json")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "appsettings.Development.json")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "appsettings.Production.json")));
        Assert.True(File.Exists(Path.Combine(projectDirectory, "appsettings.machine.HomogenizationLine.json")));
        Assert.Contains("appsettings.machine.*.json", projectFile, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_shell_appsettings_keep_security_values_as_empty_deployment_placeholders()
    {
        var root = FindRepositoryRoot();
        var projectDirectory = Path.Combine(root, "src", "Edge", "IIoT.Edge.AvaloniaShell");
        var files = new[]
        {
            "appsettings.json",
            "appsettings.machine.HomogenizationLine.json"
        };

        foreach (var fileName in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectDirectory, fileName)));
            var rootElement = document.RootElement;

            Assert.Equal(string.Empty, rootElement.GetProperty("CloudApi").GetProperty("BootstrapSecret").GetString());
            Assert.Equal(string.Empty, rootElement.GetProperty("LocalAdmin").GetProperty("PasswordHash").GetString());
        }
    }

    [AvaloniaFact]
    public void Bootstrap_registers_real_shell_menu_and_xaml_resources()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");
        var registry = provider.GetRequiredService<IAvaloniaViewRegistry>();
        var menus = registry.GetAllMenus();
        var sysMenu = provider.GetRequiredService<SysMenuViewModel>();
        var ids = StandardAvaloniaModuleViewIds.Create("Homogenization");

        Assert.Equal("Core.SysMenu", sysMenu.ViewId);
        Assert.Equal("系统导航菜单", sysMenu.ViewTitle);
        Assert.Contains(menus, item => item.ViewId == ids.Monitor);
        Assert.Contains(menus, item => item.ViewId == ids.DataView);
        Assert.Contains(menus, item => item.ViewId == ids.CapacityView);
        Assert.Contains(menus, item => item.ViewId == ids.IoView);
        Assert.Contains(menus, item => item.ViewId == ids.RecipeView);
        Assert.Contains(menus, item => item.ViewId == ids.ParamView);
        Assert.Contains(menus, item => item.ViewId == ids.HardwareConfigView);
        Assert.Contains(menus, item => item.ViewId == ids.PlcTaskBindingView);
        Assert.Contains(menus, item => item.ViewId == CoreAvaloniaViewIds.Diagnostics);
        Assert.NotEqual("Navigation_Menu_Monitor", provider.GetRequiredService<IAvaloniaLanguageService>().GetText("Navigation_Menu_Monitor"));
        Assert.Equal("匀浆出料数据", provider.GetRequiredService<IAvaloniaLanguageService>().GetText("Homogenization_Title_Data"));
    }

    [AvaloniaFact]
    public void Bootstrap_registers_startup_diagnostics_collaborators_through_di()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IStartupDiagnosticsConfigurationValidator>());
        Assert.NotNull(provider.GetRequiredService<IStartupDiagnosticsPlcDeviceValidator>());
        Assert.NotNull(provider.GetRequiredService<IStartupDiagnosticsModuleRegistrationSnapshotBuilder>());
        Assert.NotNull(provider.GetRequiredService<IStartupDiagnosticsReportBuilder>());
    }

    [AvaloniaFact]
    public void SysMenu_view_model_preserves_core_id_permissions_selection_and_login_semantics()
    {
        var registry = new FakeAvaloniaViewRegistry(
        [
            new AvaloniaMenuInfo
            {
                ViewId = "Allowed.View",
                Title = "允许菜单",
                TitleResourceKey = "Allowed_Menu",
                Icon = "Home"
            },
            new AvaloniaMenuInfo
            {
                ViewId = "Blocked.View",
                Title = "受限菜单",
                TitleResourceKey = "Missing_Menu",
                Icon = "Lock",
                RequiredPermission = "Config.Edit"
            }
        ]);
        var resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-CN"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Allowed_Menu"] = "允许菜单资源",
                ["Shell_Login"] = "登录",
                ["Shell_LogoutFormat"] = "注销 ({0})",
                ["Shell_ViewTitle_SysMenu"] = "系统导航菜单"
            },
            ["en-US"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Allowed_Menu"] = "Allowed Menu",
                ["Shell_Login"] = "Sign in",
                ["Shell_LogoutFormat"] = "Sign out ({0})",
                ["Shell_ViewTitle_SysMenu"] = "System Menu"
            }
        };
        var languageService = new AvaloniaResourceLanguageService(
            resources,
            storagePath: Path.Combine(CreateTempDirectory(), "language.json"));
        languageService.Apply("zh-CN");
        var navigation = new FakeAvaloniaNavigationService();
        var auth = new FakeAuthService();
        var permission = new FakePermissionService { CanEditParamsValue = false };
        var viewModel = new SysMenuViewModel(
            navigation,
            auth,
            permission,
            languageService,
            new ImmediateAvaloniaDispatcherService(),
            registry);
        var requestedViews = new List<string>();
        var loginRequested = false;
        viewModel.NavigationRequested += requestedViews.Add;
        viewModel.LoginRequested += () => loginRequested = true;

        Assert.Equal("Core.SysMenu", viewModel.ViewId);
        Assert.Equal("系统导航菜单", viewModel.ViewTitle);
        Assert.Equal("登录", viewModel.LoginButtonText);

        var allowed = Assert.Single(viewModel.MenuItems, item => item.ViewId == "Allowed.View");
        var blocked = Assert.Single(viewModel.MenuItems, item => item.ViewId == "Blocked.View");
        Assert.Equal("Home", allowed.Icon);
        Assert.Equal("允许菜单资源", allowed.Title);
        Assert.Equal("受限菜单", blocked.Title);
        Assert.True(allowed.IsAccessible);
        Assert.False(blocked.IsAccessible);

        blocked.NavigateCommand.Execute(null);
        Assert.Null(navigation.LastViewId);

        allowed.NavigateCommand.Execute(null);
        Assert.Equal("Allowed.View", navigation.LastViewId);
        Assert.Equal(["Allowed.View"], requestedViews);
        Assert.True(allowed.IsSelected);
        Assert.Same(allowed, viewModel.SelectedItem);

        viewModel.ExecuteLoginAction();
        Assert.True(loginRequested);
        Assert.Equal(0, auth.LogoutCalls);

        auth.SignIn("现场管理员");
        Assert.Equal("注销 (现场管理员)", viewModel.LoginButtonText);
        viewModel.ExecuteLoginAction();
        Assert.Equal(1, auth.LogoutCalls);
        Assert.False(auth.IsAuthenticated);

        permission.CanEditParamsValue = true;
        permission.RaisePermissionStateChanged();
        Assert.True(blocked.IsAccessible);
    }

    [AvaloniaFact]
    public void Bootstrap_loads_homogenization_plugin_from_catalog()
    {
        using var provider = BuildProvider();

        var module = Assert.Single(provider.GetServices<IEdgeProcessModule>());

        Assert.Equal("Homogenization", module.ModuleId);
        Assert.Equal("Homogenization", module.ProcessType);
    }

    [AvaloniaFact]
    public void Main_window_view_model_builds_dock_layout_from_registry()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");

        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        var ids = StandardAvaloniaModuleViewIds.Create("Homogenization");

        Assert.NotNull(viewModel.DockLayout);
        Assert.True(viewModel.MenuItems.Count >= 9);
        Assert.Contains(viewModel.MenuItems, item => item.ViewId == ids.Monitor);
        Assert.Contains(viewModel.MenuItems, item => item.ViewId == ids.RecipeView);
        Assert.Contains(viewModel.MenuItems, item => item.ViewId == ids.ParamView);
        var mainDock = Assert.IsType<ProportionalDock>(Assert.Single(viewModel.DockLayout.VisibleDockables!));
        var documents = Assert.IsType<DocumentDock>(Assert.Single(mainDock.VisibleDockables!));
        Assert.Equal(ids.Monitor, documents.ActiveDockable?.Id);

        viewModel.ToggleLanguageCommand.Execute(null);

        Assert.Equal("en-US", viewModel.CultureName);
        Assert.Contains(viewModel.MenuItems, item => item.ViewId == ids.Monitor && item.Title == "Monitor");
    }

    [AvaloniaFact]
    public void Main_window_layout_smoke_keeps_core_regions_available_at_1366x768()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");
        var viewModel = provider.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1366,
            Height = 768
        };

        try
        {
            window.Show();

            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.True(window.MinWidth <= 1366);
            Assert.True(window.MinHeight <= 768);
            Assert.NotNull(viewModel.DockLayout);
            Assert.NotNull(viewModel.EquipmentToolDockable);
            Assert.NotNull(viewModel.LogToolDockable);
            var shellRoot = window.FindControl<Control>("ShellRoot");
            var workspaceHost = window.FindControl<Control>("WorkspaceHost");
            var documentHost = window.FindControl<Control>("DocumentHost");
            var rightToolShell = window.FindControl<Control>("RightToolShell");
            var equipmentToolHost = window.FindControl<ContentControl>("EquipmentToolHost");
            var logToolHost = window.FindControl<ContentControl>("LogToolHost");

            Assert.NotNull(shellRoot);
            Assert.NotNull(workspaceHost);
            Assert.NotNull(documentHost);
            Assert.NotNull(rightToolShell);
            Assert.NotNull(equipmentToolHost);
            Assert.NotNull(logToolHost);
            Assert.True(workspaceHost!.Bounds.Width > 0);
            Assert.True(documentHost!.Bounds.Width > 0);
            Assert.True(rightToolShell!.Bounds.Width > 0);
            Assert.True(equipmentToolHost!.Bounds.Height > 0);
            Assert.True(logToolHost!.Bounds.Height > 0);
            Assert.True(rightToolShell.Bounds.Right <= workspaceHost.Bounds.Width + 1);

            var outputRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-visual-check");
            Directory.CreateDirectory(outputRoot);
            var bitmap = window.CaptureRenderedFrame();
            Assert.NotNull(bitmap);
            var screenshotPath = Path.Combine(outputRoot, "shell-right-tools-1366x768.png");
            bitmap!.Save(screenshotPath);
            Assert.True(File.Exists(screenshotPath), $"Shell screenshot was not written to {screenshotPath}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Startup_error_window_without_visible_owner_should_not_throw()
    {
        var owner = new Window();
        var dialog = new StartupErrorWindow("启动校验失败。", "模块数：0", "diagnostics.log");

        var showTask = dialog.ShowSafelyAsync(owner);

        try
        {
            Assert.True(dialog.IsVisible);
            Assert.Equal(WindowStartupLocation.CenterScreen, dialog.WindowStartupLocation);

            dialog.Close();
            await showTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            if (dialog.IsVisible)
            {
                dialog.Close();
            }
        }
    }

    [AvaloniaFact]
    public void Startup_error_window_module_mismatch_uses_fixed_operator_fields()
    {
        var dialog = new StartupErrorWindow(
            "启动校验失败：\n- [DEVICE_MODULE_MISMATCH] (模块=Homogenization, 设备=PLC-Homogenization-01) PLC 引用了未知模块。",
            "模块数：0；PLC 设备数：1；阻断问题数：1",
            @"C:\logs");

        try
        {
            dialog.Show();
            dialog.ApplyTemplate();

            var deviceValue = FindNamedControl<TextBlock>(dialog, "StartupErrorDeviceValue");
            var moduleValue = FindNamedControl<TextBlock>(dialog, "StartupErrorModuleValue");
            var loadedModuleValue = FindNamedControl<TextBlock>(dialog, "StartupErrorLoadedModuleValue");
            var diagnosticsPathValue = FindNamedControl<TextBlock>(dialog, "StartupErrorDiagnosticsPathValue");

            Assert.NotNull(deviceValue);
            Assert.NotNull(moduleValue);
            Assert.NotNull(loadedModuleValue);
            Assert.NotNull(diagnosticsPathValue);
            Assert.Equal("PLC-Homogenization-01", deviceValue!.Text);
            Assert.Equal("Homogenization", moduleValue!.Text);
            Assert.Equal("0", loadedModuleValue!.Text);
            Assert.Equal(@"C:\logs", diagnosticsPathValue!.Text);
            Assert.Null(FindNamedControl<ScrollViewer>(dialog, "StartupErrorOuterScroll"));
        }
        finally
        {
            dialog.Close();
        }
    }

    [AvaloniaFact]
    public void Right_tool_views_use_paged_equipment_and_direct_log_list()
    {
        var equipmentView = new EquipmentView();
        var logView = new LogView();
        var window = new Window
        {
            Width = 340,
            Height = 640,
            Content = new StackPanel
            {
                Children =
                {
                    equipmentView,
                    logView
                }
            }
        };

        try
        {
            window.Show();
            equipmentView.ApplyTemplate();
            logView.ApplyTemplate();

            Assert.NotNull(FindNamedControl<ItemsControl>(equipmentView, "EquipmentPagedItems"));
            Assert.NotNull(FindNamedControl<Control>(equipmentView, "EquipmentPager"));
            Assert.NotNull(FindNamedControl<StackPanel>(logView, "LogActions"));
            Assert.Empty(logView.GetVisualDescendants().OfType<ComboBox>());
            Assert.Null(FindNamedControl<Control>(logView, "LogLatestSummary"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Navigation_service_creates_registered_pages()
    {
        using var provider = BuildProvider();
        var navigation = provider.GetRequiredService<IAvaloniaNavigationService>();
        var ids = StandardAvaloniaModuleViewIds.Create("Homogenization");

        foreach (var viewId in new[]
                 {
                     ids.Monitor,
                     ids.DataView,
                     ids.CapacityView,
                     ids.IoView,
                     ids.RecipeView,
                     ids.ParamView,
                     ids.HardwareConfigView,
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
    public void Host_pages_layout_smoke_keeps_key_regions_available_at_1366x768()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");
        var pageCases = CreateHostPageVisualCases();

        foreach (var pageCase in pageCases)
        {
            var page = pageCase.CreatePage(provider);
            var window = new Window
            {
                Width = 1366,
                Height = 768,
                Content = page
            };

            try
            {
                window.Show();
                window.ApplyTemplate();
                page.ApplyTemplate();
                AssertHostPageKeyRegions(pageCase, page);
            }
            finally
            {
                window.Content = null;
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void Host_pages_visual_check_writes_direct_1366x768_screenshots()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");
        var outputRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-visual-check");
        Directory.CreateDirectory(outputRoot);

        foreach (var pageCase in CreateHostPageVisualCases())
        {
            var page = pageCase.CreatePage(provider);
            var window = new Window
            {
                Width = 1366,
                Height = 768,
                Content = page
            };

            try
            {
                window.Show();
                window.ApplyTemplate();
                page.ApplyTemplate();
                AssertHostPageKeyRegions(pageCase, page);

                var bitmap = window.CaptureRenderedFrame();
                Assert.NotNull(bitmap);

                var screenshotPath = Path.Combine(outputRoot, $"host-page-direct-1366x768-{pageCase.Slug}.png");
                bitmap!.Save(screenshotPath);

                var screenshot = new FileInfo(screenshotPath);
                Assert.True(screenshot.Exists, $"{pageCase.Name} screenshot was not written to {screenshotPath}");
                Assert.True(screenshot.Length > 0, $"{pageCase.Name} screenshot is empty: {screenshotPath}");
            }
            finally
            {
                window.Content = null;
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void Monitor_visual_check_writes_1366x768_screenshot()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");
        var ids = StandardAvaloniaModuleViewIds.Create("Homogenization");
        var page = CreatePage<MonitorViewPage, MonitorViewModel>(
            provider,
            ids.Monitor,
            "Navigation_Menu_Monitor",
            "Monitor");
        var window = new Window
        {
            Width = 1366,
            Height = 768,
            Content = page
        };
        var outputRoot = Path.Combine(Path.GetTempPath(), "iiot-avalonia-visual-check");
        Directory.CreateDirectory(outputRoot);

        try
        {
            window.Show();
            window.ApplyTemplate();
            page.ApplyTemplate();

            Assert.NotNull(FindNamedControl<Control>(page, "MonitorProcessFlowCard"));
            Assert.NotNull(FindNamedControl<Control>(page, "MonitorRightStatusPanel"));

            var bitmap = window.CaptureRenderedFrame();
            Assert.NotNull(bitmap);

            var screenshotPath = Path.Combine(outputRoot, "monitor-refactor-1366x768.png");
            bitmap!.Save(screenshotPath);

            var screenshot = new FileInfo(screenshotPath);
            Assert.True(screenshot.Exists, $"Monitor screenshot was not written to {screenshotPath}");
            Assert.True(screenshot.Length > 0, $"Monitor screenshot is empty: {screenshotPath}");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Diagnostics_page_exposes_field_acceptance_summary_as_readonly_grid()
    {
        var page = new DiagnosticsPage();

        var grid = page.FindControl<DataGrid>("FieldAcceptanceSummaryGrid");

        Assert.NotNull(grid);
        Assert.True(grid!.IsReadOnly);
        Assert.False(grid.AutoGenerateColumns);
        Assert.Equal(3, grid.Columns.Count);
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
    public async Task Hardware_config_page_loads_service_data_and_saves_after_confirm()
    {
        using var provider = BuildProvider();
        var crud = new FakeHardwareConfigCrudService();
        var dialog = new FakeAvaloniaDialogService { ConfirmResult = true };
        var viewModel = new HardwareConfigViewModel(
            crud,
            provider.GetRequiredService<IAvaloniaLanguageService>(),
            dialog,
            "test.hardware",
            "Navigation_Title_HardwareConfig",
            "硬件配置");

        await viewModel.OnActivatedAsync();

        Assert.Equal("PLC-01", Assert.Single(viewModel.NetworkDevices).DeviceName);
        Assert.Single(viewModel.SerialDevices);
        Assert.Equal(3, viewModel.IoMappings.Count);
        Assert.NotEmpty(viewModel.CandidateIoSignals);

        var originalNetworkCount = viewModel.NetworkDevices.Count;
        viewModel.AddNetworkDeviceCommand.Execute(null);
        Assert.Equal(originalNetworkCount + 1, viewModel.NetworkDevices.Count);

        var originalMappingCount = viewModel.IoMappings.Count;
        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);
        Assert.True(viewModel.IsDialogOpen);
        viewModel.ConfirmDialogCommand.Execute(null);
        Assert.False(viewModel.IsDialogOpen);
        Assert.True(viewModel.IoMappings.Count > originalMappingCount);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(dialog.LastConfirmMessage?.Contains("I/O") == true);
        Assert.NotEmpty(crud.SavedNetworkDevices);
        Assert.NotEmpty(crud.SavedSerialDevices);
        Assert.NotEmpty(crud.SavedIoMappings);
    }

    [AvaloniaFact]
    public async Task Io_view_reads_config_shape_and_runtime_snapshot_without_real_plc_write()
    {
        using var provider = BuildProvider();
        var crud = new FakeHardwareConfigCrudService();
        var plcManager = new FakePlcConnectionManager();
        var plcDataStore = new FakePlcDataStore();
        var runtimeState = new AvaloniaRuntimeState();
        var dialog = new FakeAvaloniaDialogService { ConfirmResult = true };
        var permission = new FakePermissionService { CanEditHardwareValue = true };
        var auditStore = new IoViewWriteGateAuditStore();
        var traceStore = new FakePlcIoWriteTraceStore();
        var languageService = provider.GetRequiredService<IAvaloniaLanguageService>();
        var safePort = new RuntimeBufferIoViewSafeInteractionPort(
            runtimeState,
            permission,
            plcManager,
            plcDataStore,
            dialog,
            languageService,
            provider.GetRequiredService<ILogService>(),
            auditStore);
        var viewModel = new IoViewViewModel(
            crud,
            plcManager,
            plcDataStore,
            languageService,
            safePort,
            permission,
            runtimeState,
            traceStore);

        await viewModel.OnActivatedAsync();

        Assert.NotNull(viewModel.SelectedDevice);
        Assert.True(viewModel.HasInteractionRows);
        Assert.True(viewModel.HasDataSections);

        await viewModel.ManualReadCommand.ExecuteAsync(null);
        Assert.Contains("未启动", viewModel.FeedbackMessage);
        Assert.Equal("未启动", viewModel.SnapshotSourceText);

        runtimeState.SetStatus(AvaloniaRuntimeStatus.Running, "测试运行链路已启动。");

        await viewModel.ManualReadCommand.ExecuteAsync(null);
        Assert.Contains("暂无运行时快照", viewModel.FeedbackMessage);
        Assert.Equal("无快照", viewModel.SnapshotSourceText);

        plcDataStore.Buffer = new FakePlcBuffer(new Dictionary<string, ushort[]>
        {
            ["Start.Request"] = [7],
            ["Weight.Current"] = [123]
        });
        plcManager.Snapshot = new PlcConnectionRuntimeSnapshot
        {
            NetworkDeviceId = 1,
            DeviceName = "PLC-01",
            IsConnected = true
        };

        await viewModel.ManualReadCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsConnected);
        Assert.Contains("运行时快照", viewModel.SnapshotSourceText, StringComparison.Ordinal);
        Assert.NotEqual("--", viewModel.SnapshotRefreshText);
        Assert.Equal("7", viewModel.InteractionRows[0].PlcValueText);

        var row = viewModel.InteractionRows.First();
        Assert.NotNull(row.WriteCommand);
        row.WriteValue = 9;
        await row.WriteCommand.ExecuteAsync(null);
        Assert.Contains("已进入运行时缓冲", viewModel.FeedbackMessage);
        Assert.Equal("9", row.HostReplyValueText);
        Assert.Contains("已进入运行时缓冲，等待扫描任务按块写入", row.LastPlcWriteTraceText);
        Assert.Contains("已进入运行时缓冲，等待扫描任务按块写入", Assert.Single(auditStore.GetRecent()).Message);

        traceStore.Record(new PlcIoWriteTraceEntry(
            DateTimeOffset.Now,
            PlcIoWriteTraceKind.Success,
            1,
            "PLC-01",
            "D101",
            1,
            ["Start.Reply"],
            null));
        await viewModel.ManualReadCommand.ExecuteAsync(null);
        Assert.Contains("PLC 块写入成功", row.LastPlcWriteTraceText);
    }

    [AvaloniaFact]
    public async Task Recipe_view_uses_application_contract_with_fake_service()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");
        var crud = new FakeRecipeViewCrudService();
        var recipeService = new FakeRecipeService();
        var viewModel = new RecipeViewModel(
            crud,
            recipeService,
            provider.GetRequiredService<IAvaloniaLanguageService>(),
            provider.GetRequiredService<IAvaloniaDialogService>(),
            provider.GetRequiredService<IAvaloniaDispatcherService>(),
            "test.recipe",
            "Navigation_Title_ProductRecipe",
            "产品配方");

        await viewModel.OnActivatedAsync();

        Assert.Equal("R-001", viewModel.RecipeName);
        Assert.Single(viewModel.Params);
        Assert.True(viewModel.IsLocalAdmin);

        await viewModel.SwitchSourceCommand.ExecuteAsync(null);
        Assert.Equal(RecipeSource.Local, crud.LastSwitchSource);

        viewModel.EditKey = "Pressure";
        viewModel.EditMin = "1.5";
        viewModel.EditMax = "2.5";
        viewModel.EditUnit = "MPa";
        await viewModel.SaveLocalParamCommand.ExecuteAsync(null);
        Assert.Equal("Pressure", crud.LastSavedKey);
        Assert.Equal(1.5d, crud.LastSavedMin);
        Assert.Equal(2.5d, crud.LastSavedMax);

        await viewModel.Params[0].DeleteCommand.ExecuteAsync(null);
        Assert.Equal("Speed", crud.LastDeletedKey);
    }

    [AvaloniaFact]
    public async Task Param_view_loads_groups_saves_through_application_contract_and_tracks_permission()
    {
        using var provider = BuildProvider();
        provider.GetRequiredService<IAvaloniaLanguageService>().Apply("zh-CN");
        var crud = new FakeParamViewCrudService();
        var permission = new FakePermissionService { CanEditParamsValue = true };
        var viewModel = new ParamViewModel(
            crud,
            permission,
            provider.GetRequiredService<IAvaloniaLanguageService>(),
            provider.GetRequiredService<IAvaloniaDialogService>(),
            provider.GetRequiredService<IAvaloniaDispatcherService>(),
            "test.param",
            "Navigation_Title_ParamConfig",
            "参数配置");

        await viewModel.OnActivatedAsync();

        Assert.Single(viewModel.MesParamGroups);
        Assert.Equal("上报地址", viewModel.MesParamGroups[0].Params[0].DisplayName);
        Assert.True(viewModel.CanEdit);

        viewModel.MesParamGroups[0].Params[0].Value = "http://mes.local";
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Single(crud.SavedParams);
        Assert.Equal("http://mes.local", crud.SavedParams[0].Value);

        permission.CanEditParamsValue = false;
        permission.RaisePermissionStateChanged();
        await provider.GetRequiredService<IAvaloniaDispatcherService>().InvokeAsync(() => { });
        Assert.False(viewModel.CanEdit);
        Assert.False(viewModel.SaveCommand.CanExecute(null));
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
    public async Task Homogenization_plugin_data_view_reads_context_and_controls_timer()
    {
        var context = new HomogenizationContext();
        context.RecordOutbound(new HomogenizationCellData
        {
            TrayCode = "T-001",
            InboundTime = new DateTime(2026, 5, 13, 8, 0, 0),
            CompletedTime = new DateTime(2026, 5, 13, 8, 5, 0),
            RuntimeStatus = "出料待上传",
            CntActualKg = 1.2d,
            NmpActualKg = 3.4d
        });

        var languageService = CreateLanguageService(typeof(HomogenizationDataViewModel).Assembly);
        var timerFactory = new FakeAvaloniaTimerFactory();
        var viewModel = new HomogenizationDataViewModel(
            new FakeProductionContextStore([context]),
            languageService,
            new ImmediateAvaloniaDispatcherService(),
            timerFactory,
            Options.Create(new HomogenizationModuleOptions()));

        await viewModel.OnActivatedAsync();

        Assert.True(timerFactory.LastTimer?.IsEnabled);
        var row = Assert.Single(viewModel.Records);
        Assert.Equal("T-001", row.TrayCode);
        Assert.Equal("1.2", row.CntActual);
        Assert.Equal("共 1 条出料记录。", viewModel.StatusText);

        await viewModel.OnDeactivatedAsync();

        Assert.False(timerFactory.LastTimer?.IsEnabled);
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
        var runtimeState = provider.GetRequiredService<IAvaloniaRuntimeState>();
        var header = provider.GetRequiredService<HeaderViewModel>();
        var footer = provider.GetRequiredService<FooterViewModel>();
        runtimeState.SetStatus(AvaloniaRuntimeStatus.Running, "运行链路已启动。", "模块数：1；PLC 设备数：1；阻断问题数：0；运行目录：test");
        Assert.Equal("运行中", header.RuntimeStatusText);
        Assert.Equal("运行中", footer.RuntimeStatusText);
        Assert.Contains("模块数：1", footer.DiagnosticsSummary, StringComparison.Ordinal);
        Assert.NotNull(provider.GetRequiredService<LoginViewModel>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection()
            .AddEdgeHostAvaloniaBootstrap(CreateOptions())
            .AddSingleton<MainWindowViewModel>()
            .BuildServiceProvider();

        return services;
    }

    private static IAvaloniaLanguageService CreateLanguageService(params Assembly[] assemblies)
    {
        var service = new AvaloniaResourceLanguageService(new AvaloniaXamlStringResourceLoader().Load(assemblies));
        service.Apply("zh-CN");
        return service;
    }

    private static TView CreatePage<TView, TViewModel>(
        IServiceProvider provider,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        where TView : Control, new()
        where TViewModel : NavigationPageViewModelBase
    {
        return new TView
        {
            DataContext = ActivatorUtilities.CreateInstance<TViewModel>(
                provider,
                viewId,
                titleResourceKey,
                titleFallback)
        };
    }

    private static HostPageVisualCase[] CreateHostPageVisualCases()
    {
        var ids = StandardAvaloniaModuleViewIds.Create("Homogenization");

        return
        [
            new HostPageVisualCase(
                "CapacityViewPage",
                "capacity",
                sp => CreatePage<CapacityViewPage, CapacityViewModel>(sp, ids.CapacityView, "Navigation_Menu_Capacity", "Capacity"),
                "CapacityPageRoot",
                ["CapacityToolbar", "CapacityRecordsGrid"]),
            new HostPageVisualCase(
                "DataViewPage",
                "data",
                sp => CreatePage<DataViewPage, DataViewModel>(sp, ids.DataView, "Navigation_Menu_Data", "Data"),
                "DataPageRoot",
                ["DataToolbar", "DataRecordsGrid"]),
            new HostPageVisualCase(
                "RecipeViewPage",
                "recipe",
                sp => CreatePage<RecipeViewPage, RecipeViewModel>(sp, ids.RecipeView, "Navigation_Menu_Recipe", "Recipe"),
                "RecipePageRoot",
                ["RecipeSummaryPanel", "RecipeParamsGrid", "RecipeEmergencyEdit"]),
            new HostPageVisualCase(
                "ParamViewPage",
                "param",
                sp => CreatePage<ParamViewPage, ParamViewModel>(sp, ids.ParamView, "Navigation_Menu_ParamConfig", "Parameters"),
                "ParamPageRoot",
                ["ParamTabs"]),
            new HostPageVisualCase(
                "PlcTaskBindingPage",
                "plc-task-binding",
                sp => CreatePage<PlcTaskBindingPage, PlcTaskBindingViewModel>(sp, ids.PlcTaskBindingView, "Navigation_Menu_PlcTaskBinding", "PLC Tasks"),
                "PlcTaskBindingPageRoot",
                ["PlcDeviceToolbar", "PlcTaskBindingGrid"])
        ];
    }

    private static void AssertHostPageKeyRegions(HostPageVisualCase pageCase, Control page)
    {
        var availableNames = $"{page.GetType().FullName}; {DescribeContent(page)}; {GetControlNames(page)}";
        var root = FindNamedControl<Control>(page, pageCase.RootName);
        Assert.True(root is not null, $"{pageCase.Name} missing {pageCase.RootName}; names: {availableNames}");
        Assert.True(root!.Bounds.Width > 0);
        Assert.True(root.Bounds.Height > 0);

        foreach (var controlName in pageCase.RequiredControls)
        {
            Assert.True(
                FindNamedControl<Control>(page, controlName) is not null,
                $"{pageCase.Name} missing {controlName}; names: {availableNames}");
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "iiot-edge-avalonia-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static T? FindNamedControl<T>(Control root, string name)
        where T : Control
    {
        if (root.Name == name && root is T typedRoot)
        {
            return typedRoot;
        }

        var field = root.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.GetValue(root) is T namedField)
        {
            return namedField;
        }

        if (root is ContentControl { Content: Control content })
        {
            var foundInContent = FindNamedControl<T>(content, name);
            if (foundInContent is not null)
            {
                return foundInContent;
            }
        }

        if (root is ContentPresenter { Content: Control presentedContent })
        {
            var foundInPresentedContent = FindNamedControl<T>(presentedContent, name);
            if (foundInPresentedContent is not null)
            {
                return foundInPresentedContent;
            }
        }

        return root.GetVisualDescendants()
            .OfType<T>()
            .Concat(root.GetLogicalDescendants().OfType<T>())
            .FirstOrDefault(control => control.Name == name);
    }

    private static string GetControlNames(Control root)
    {
        var names = EnumerateControls(root)
            .Select(control => control.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

        return string.Join(", ", names);
    }

    private static string DescribeContent(Control root)
    {
        return root switch
        {
            ContentControl { Content: not null } contentControl => $"content={contentControl.Content.GetType().FullName}",
            ContentPresenter { Content: not null } presenter => $"presented={presenter.Content.GetType().FullName}",
            _ => "content=<none>"
        };
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;

        if (root is ContentControl { Content: Control content })
        {
            foreach (var child in EnumerateControls(content))
            {
                yield return child;
            }
        }

        if (root is ContentPresenter { Content: Control presentedContent })
        {
            foreach (var child in EnumerateControls(presentedContent))
            {
                yield return child;
            }
        }

        foreach (var child in root.GetVisualDescendants().OfType<Control>())
        {
            yield return child;
        }

        foreach (var child in root.GetLogicalDescendants().OfType<Control>())
        {
            yield return child;
        }
    }

    private sealed record HostPageVisualCase(
        string Name,
        string Slug,
        Func<IServiceProvider, Control> CreatePage,
        string RootName,
        string[] RequiredControls);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Cannot locate IIoT.EdgeClient.AvaloniaMigration repository root.");
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
            ["Homogenization"],
            PluginDirectories: [AppContext.BaseDirectory]);
    }

    private sealed class FakeProductionContextStore : IProductionContextStore
    {
        private readonly IReadOnlyCollection<ProductionContext> _contexts;

        public FakeProductionContextStore(IReadOnlyCollection<ProductionContext> contexts)
        {
            _contexts = contexts;
        }

        public ProductionContext GetOrCreate(string deviceName)
            => _contexts.FirstOrDefault() ?? new ProductionContext { DeviceName = deviceName };

        public ProductionContext GetOrCreate(string deviceName, string? moduleId)
            => GetOrCreate(deviceName);

        public IReadOnlyCollection<ProductionContext> GetAll()
            => _contexts;

        public ProductionContextPersistenceDiagnostics GetPersistenceDiagnostics()
            => new(0, null);

        public void LoadFromFile()
        {
        }

        public void SaveToFile()
        {
        }

        public Task StartAutoSaveAsync(CancellationToken ct, int intervalSeconds = 30)
            => Task.CompletedTask;
    }

    private sealed class FakeAvaloniaTimerFactory : IAvaloniaTimerFactory
    {
        public FakeAvaloniaTimer? LastTimer { get; private set; }

        public IAvaloniaTimer Create(TimeSpan interval)
        {
            LastTimer = new FakeAvaloniaTimer { Interval = interval };
            return LastTimer;
        }
    }

    private sealed class FakeAvaloniaTimer : IAvaloniaTimer
    {
        public event EventHandler? Tick;

        public TimeSpan Interval { get; set; }

        public bool IsEnabled { get; private set; }

        public void Start() => IsEnabled = true;

        public void Stop() => IsEnabled = false;

        public void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ImmediateAvaloniaDispatcherService : IAvaloniaDispatcherService
    {
        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAvaloniaViewRegistry : IAvaloniaViewRegistry
    {
        private readonly IReadOnlyList<AvaloniaMenuInfo> _menus;

        public FakeAvaloniaViewRegistry(IReadOnlyList<AvaloniaMenuInfo> menus)
        {
            _menus = menus;
        }

        public void RegisterRoute(
            string viewId,
            Type viewType,
            Type viewModelType,
            Func<IServiceProvider, object>? viewModelFactory = null,
            bool cacheView = true)
            => throw new NotSupportedException();

        public void RegisterMenu(AvaloniaMenuInfo menuInfo)
            => throw new NotSupportedException();

        public void RegisterDockPane(
            AvaloniaDockPaneInfo info,
            Type viewType,
            Type viewModelType,
            Func<IServiceProvider, object>? viewModelFactory = null,
            bool cacheView = true)
            => throw new NotSupportedException();

        public AvaloniaViewRegistration? GetViewRegistration(string viewId) => null;

        public IReadOnlyList<AvaloniaViewRegistration> GetAllViewRegistrations() => [];

        public IReadOnlyList<AvaloniaMenuInfo> GetAllMenus() => _menus;

        public IReadOnlyList<AvaloniaDockPaneInfo> GetAllDockPanes() => [];
    }

    private sealed class FakeAvaloniaNavigationService : IAvaloniaNavigationService
    {
        public string? LastViewId { get; private set; }

        public object? CurrentViewModel => null;

        public Control? CurrentView => null;

        public event Action<object?>? Navigated
        {
            add { }
            remove { }
        }

        public void NavigateTo(string viewId)
        {
            LastViewId = viewId;
        }
    }

    private sealed class FakeAuthService : IAuthService
    {
        public UserSession? CurrentUser { get; private set; }

        public bool IsAuthenticated => CurrentUser is not null;

        public int LogoutCalls { get; private set; }

        public event Action<UserSession?>? AuthStateChanged;

        public bool HasPermission(string permission)
            => CurrentUser?.Permissions.Contains(permission) == true;

        public Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(IsAuthenticated);

        public Task<AuthResult> LoginLocalAsync(string password)
            => Task.FromResult(AuthResult.Fail("not used"));

        public Task<AuthResult> LoginCloudAsync(
            string employeeNo,
            string password,
            Guid deviceId)
            => Task.FromResult(AuthResult.Fail("not used"));

        public void Logout()
        {
            LogoutCalls++;
            CurrentUser = null;
            AuthStateChanged?.Invoke(null);
        }

        public void SignIn(string displayName)
        {
            CurrentUser = new UserSession
            {
                DisplayName = displayName,
                EmployeeNo = "E001",
                IsLocalAdmin = true
            };
            AuthStateChanged?.Invoke(CurrentUser);
        }
    }

    private sealed class FakeRecipeViewCrudService : IRecipeViewCrudService
    {
        private RecipeViewSnapshot _snapshot = new(
            "R-001",
            "V1",
            "Homogenization",
            "2026-05-13",
            true,
            [new RecipeParamItemDto { Name = "Speed", Min = "10", Max = "20", Unit = "rpm" }]);

        public RecipeSource? LastSwitchSource { get; private set; }

        public string? LastSavedKey { get; private set; }

        public double? LastSavedMin { get; private set; }

        public double? LastSavedMax { get; private set; }

        public string? LastDeletedKey { get; private set; }

        public Task<RecipeViewSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<RecipeViewSnapshot?>(_snapshot);

        public Task<bool> GetIsLocalAdminAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> SyncCloudAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task SwitchSourceAsync(RecipeSource source, CancellationToken cancellationToken = default)
        {
            LastSwitchSource = source;
            _snapshot = _snapshot with { IsCloudSource = source == RecipeSource.Cloud };
            return Task.CompletedTask;
        }

        public Task SaveLocalParamAsync(
            string key,
            double? min,
            double? max,
            string unit,
            CancellationToken cancellationToken = default)
        {
            LastSavedKey = key;
            LastSavedMin = min;
            LastSavedMax = max;
            return Task.CompletedTask;
        }

        public Task DeleteLocalParamAsync(string key, CancellationToken cancellationToken = default)
        {
            LastDeletedKey = key;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRecipeService : IRecipeService
    {
        public RecipeSource ActiveSource { get; private set; } = RecipeSource.Cloud;

        public RecipeData? ActiveRecipe => null;

        public RecipeData? CloudRecipe => null;

        public RecipeData? LocalRecipe => null;

        public event Action? RecipeChanged;

        public void SwitchSource(RecipeSource source)
        {
            ActiveSource = source;
            RecipeChanged?.Invoke();
        }

        public RecipeParam? GetParam(string name) => null;

        public IReadOnlyDictionary<string, RecipeParam> GetAllParams()
            => new Dictionary<string, RecipeParam>();

        public Task<bool> PullFromCloudAsync() => Task.FromResult(false);

        public void SetLocalParam(string name, double? min, double? max, string unit)
        {
        }

        public void RemoveLocalParam(string name)
        {
        }

        public void LoadFromFile()
        {
        }

        public void SaveToFile()
        {
        }
    }

    private sealed class FakeParamViewCrudService : IParamViewCrudService
    {
        public List<ModuleParamVm> SavedParams { get; } = [];

        public Task<ParamViewInitResult> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ParamViewInitResult(
                [CreateGroup(ModuleParamCategory.Mes, "上报地址", "http://127.0.0.1")],
                [],
                []));

        public Task<CrudOperationResult> SaveAsync(
            IReadOnlyCollection<ModuleParamVm> moduleParams,
            CancellationToken cancellationToken = default)
        {
            SavedParams.Clear();
            SavedParams.AddRange(moduleParams);
            return Task.FromResult(CrudOperationResult.Success("saved"));
        }

        private static ModuleParamGroupVm CreateGroup(
            ModuleParamCategory category,
            string displayName,
            string value)
        {
            var group = new ModuleParamGroupVm
            {
                ModuleId = "Homogenization",
                ModuleDisplayName = "匀浆"
            };
            group.Params.Add(new ModuleParamVm
            {
                ModuleId = "Homogenization",
                Category = category,
                Key = "Homogenization.Mes.Endpoint",
                Name = "Endpoint",
                DisplayNameFallback = displayName,
                DisplayName = displayName,
                DescriptionFallback = "MES 地址",
                Description = "MES 地址",
                ValueKind = ParamValueKind.String,
                Value = value,
                DefaultValue = value,
                Unit = string.Empty,
                Min = string.Empty,
                Max = string.Empty
            });

            return group;
        }
    }

    private sealed class FakeHardwareConfigCrudService : IHardwareConfigCrudService
    {
        private readonly List<NetworkDeviceVm> _networkDevices =
        [
            new()
            {
                Id = 1,
                DeviceName = "PLC-01",
                DeviceType = DeviceType.PLC,
                DeviceModel = "S7-1200",
                ModuleId = "Homogenization",
                IpAddress = "192.168.1.10",
                Port1 = 102,
                ConnectTimeout = 3000,
                IsEnabled = true
            }
        ];

        private readonly List<SerialDeviceVm> _serialDevices =
        [
            new()
            {
                Id = 1,
                DeviceName = "Scale-01",
                DeviceType = "Scale",
                PortName = "COM1",
                BaudRate = 9600,
                DataBits = 8,
                StopBits = "One",
                Parity = "None",
                IsEnabled = true
            }
        ];

        private readonly List<IoMappingVm> _ioMappings =
        [
            new()
            {
                Id = 1,
                NetworkDeviceId = 1,
                SignalKey = "Start.Request",
                PlcAddress = "D100",
                AddressCount = 1,
                Category = "Interaction",
                BusinessGroup = "Start",
                SignalName = "启动请求",
                DataType = "Bool",
                Direction = "Read",
                SortOrder = 1
            },
            new()
            {
                Id = 2,
                NetworkDeviceId = 1,
                SignalKey = "Start.Reply",
                PlcAddress = "D101",
                AddressCount = 1,
                Category = "Interaction",
                BusinessGroup = "Start",
                SignalName = "启动应答",
                DataType = "Bool",
                Direction = "Write",
                SortOrder = 2
            },
            new()
            {
                Id = 3,
                NetworkDeviceId = 1,
                SignalKey = "Weight.Current",
                PlcAddress = "D200",
                AddressCount = 1,
                Category = "SingleRead",
                BusinessGroup = "Weight",
                SignalName = "当前重量",
                DataType = "Int16",
                Direction = "Read",
                SortOrder = 3
            }
        ];

        public List<NetworkDeviceVm> SavedNetworkDevices { get; } = [];

        public List<SerialDeviceVm> SavedSerialDevices { get; } = [];

        public List<IoMappingVm> SavedIoMappings { get; } = [];

        public Task<HardwareConfigInitResult> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new HardwareConfigInitResult(_networkDevices.ToList(), _serialDevices.ToList()));

        public Task<IoMappingPageResult> LoadIoMappingsAsync(int networkDeviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(new IoMappingPageResult(
                _ioMappings.Where(mapping => mapping.NetworkDeviceId == networkDeviceId).ToList(),
                _ioMappings.Count(mapping => mapping.NetworkDeviceId == networkDeviceId)));

        public Task<ModuleTemplateInfoResult> GetModuleTemplateInfoAsync(
            NetworkDeviceVm? selectedNetworkDevice,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ModuleIoTemplateEntry> candidates =
            [
                new(
                    "Start.Request",
                    "D100",
                    1,
                    "Bool",
                    "Read",
                    1,
                    Category: "Interaction",
                    BusinessGroup: "Start",
                    SignalName: "启动请求"),
                new(
                    "Weight.Current",
                    "D200",
                    1,
                    "Int16",
                    "Read",
                    3,
                    Category: "SingleRead",
                    BusinessGroup: "Weight",
                    SignalName: "当前重量")
            ];

            return Task.FromResult(new ModuleTemplateInfoResult(true, "Homogenization", candidates, candidates, "ok"));
        }

        public Task<CrudOperationResult> ApplyModuleTemplateAsync(
            NetworkDeviceVm? selectedNetworkDevice,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CrudOperationResult.Success("ok"));

        public Task<CrudOperationResult> SaveAsync(
            IReadOnlyCollection<NetworkDeviceVm> networkDevices,
            IReadOnlyCollection<SerialDeviceVm> serialDevices,
            int selectedNetworkDeviceId,
            IReadOnlyCollection<IoMappingVm> ioMappings,
            CancellationToken cancellationToken = default)
        {
            SavedNetworkDevices.Clear();
            SavedNetworkDevices.AddRange(networkDevices);
            SavedSerialDevices.Clear();
            SavedSerialDevices.AddRange(serialDevices);
            SavedIoMappings.Clear();
            SavedIoMappings.AddRange(ioMappings);
            return Task.FromResult(CrudOperationResult.Success("saved"));
        }
    }

    private sealed class FakeAvaloniaDialogService : IAvaloniaDialogService
    {
        public event EventHandler<AvaloniaDialogRequest>? DialogRequested;

        public bool ConfirmResult { get; set; }

        public string? LastConfirmMessage { get; private set; }

        public Task ShowInfoAsync(string title, string message)
        {
            DialogRequested?.Invoke(this, AvaloniaDialogRequest.CreateInfo(title, message));
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message)
        {
            LastConfirmMessage = message;
            var request = AvaloniaDialogRequest.CreateConfirm(title, message);
            request.Complete(ConfirmResult);
            DialogRequested?.Invoke(this, request);
            return Task.FromResult(ConfirmResult);
        }
    }

    private sealed class FakePlcConnectionManager : IPlcConnectionManager
    {
        public PlcConnectionRuntimeSnapshot? Snapshot { get; set; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReloadAsync(string deviceName, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default) => Task.CompletedTask;

        public void RegisterTasks(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
        {
        }

        public IPlcService? GetPlc(int networkDeviceId) => null;

        public ProductionContext? GetContext(string deviceName) => null;

        public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
        {
        }

        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId) => Snapshot;

        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses()
            => Snapshot is null ? [] : [Snapshot];

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePlcDataStore : IPlcDataStore
    {
        public IPlcBufferTransport? Buffer { get; set; }

        public void Register(int networkDeviceId, int readSize, int writeSize)
        {
        }

        public void Register(
            int networkDeviceId,
            int readSize,
            int writeSize,
            IReadOnlyCollection<PlcBufferSignalBinding> signalBindings)
        {
        }

        public IPlcBufferTransport? GetBuffer(int networkDeviceId) => Buffer;

        public bool HasDevice(int networkDeviceId) => Buffer is not null;
    }

    private sealed class FakePlcIoWriteTraceStore : IPlcIoWriteTraceStore
    {
        private readonly List<PlcIoWriteTraceEntry> _entries = [];

        public void Record(PlcIoWriteTraceEntry entry)
            => _entries.Insert(0, entry);

        public IReadOnlyList<PlcIoWriteTraceEntry> GetRecent(int count = 50)
            => _entries.Take(Math.Max(1, count)).ToArray();

        public PlcIoWriteTraceEntry? GetLatestForSignals(int deviceId, IReadOnlyCollection<string> signalKeys)
        {
            var keys = signalKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _entries.FirstOrDefault(entry =>
                entry.DeviceId == deviceId &&
                entry.SignalKeys.Any(keys.Contains));
        }
    }

    private sealed class FakePlcBuffer : IPlcBufferTransport
    {
        private readonly IReadOnlyDictionary<string, ushort[]> _readSignals;
        private readonly Dictionary<string, ushort[]> _writeSignals = new(StringComparer.OrdinalIgnoreCase);

        public FakePlcBuffer(IReadOnlyDictionary<string, ushort[]> readSignals)
        {
            _readSignals = readSignals;
        }

        public event EventHandler<PlcSignalBufferChangedEventArgs>? SignalValuesChanged;

        public ushort GetReadValue(int index) => 0;

        public bool TryGetReadWords(string signalKey, out ushort[] values)
        {
            if (_readSignals.TryGetValue(signalKey, out var words))
            {
                values = words;
                return true;
            }

            values = [];
            return false;
        }

        public bool TryGetWriteWords(string signalKey, out ushort[] values)
        {
            if (_writeSignals.TryGetValue(signalKey, out var words))
            {
                values = words;
                return true;
            }

            values = [];
            return false;
        }

        public void SetWriteValue(int index, ushort value)
        {
        }

        public void SetWriteValue(string signalKey, int offset, ushort value)
        {
            _writeSignals[signalKey] = [value];
            SignalValuesChanged?.Invoke(this, new PlcSignalBufferChangedEventArgs(signalKey, "Write"));
        }

        public void UpdateReadBuffer(ushort[] data)
        {
        }

        public void UpdateReadSignal(string signalKey, IReadOnlyList<ushort> data)
        {
        }

        public ushort[] GetWriteBuffer() => [];

        public void SetSignalBindings(IReadOnlyCollection<PlcBufferSignalBinding> bindings)
        {
        }
    }

    private sealed class FakePermissionService : IClientPermissionService
    {
        public bool CanEditParamsValue { get; set; }

        public bool CanEditParams => CanEditParamsValue;

        public bool CanEditHardwareValue { get; set; } = true;

        public bool CanEditHardware => CanEditHardwareValue;

        public bool IsLocalAdmin => true;

        public event Action? PermissionStateChanged;

        public bool HasPermission(string permission) => CanEditParamsValue;

        public void RaisePermissionStateChanged() => PermissionStateChanged?.Invoke();
    }
}
