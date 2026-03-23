using IIoT.Edge.CloudSync;
using IIoT.Edge.Contracts;
using IIoT.Edge.Contracts.Device;
using IIoT.Edge.Infrastructure;
using IIoT.Edge.Module.Hardware;
using IIoT.Edge.Module.Hardware.HardwareConfigView.Mappings;
using IIoT.Edge.PlcDevice;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.UI.Shared;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.Widgets.Footer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;

namespace IIoT.Edge.Shell
{
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; private set; } = null!;

        public App()
        {
            _ = typeof(MaterialDesignThemes.Wpf.BundledTheme).Assembly;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            var services = new ServiceCollection();
            var viewRegistry = new ViewRegistry();

            // 1. 模块扫描
            var machineModule = configuration["MachineModule"];
            IModuleLoader loader = new ModuleLoader(services, viewRegistry);
            loader.LoadFromDirectory(AppDomain.CurrentDomain.BaseDirectory, machineModule);

            // 2. 各层注入
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IIoT.Edge", "edge.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);  // 加这行
            services.AddAutoMapper(cfg => cfg.AddProfile<HardwareMappingProfile>());
            services.AddSingleton<IViewRegistry>(viewRegistry);
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddInfrastructure(dbPath);
            services.AddCloudSync(configuration);
            services.AddShellWidgets();
            services.AddShell();
            services.AddPlcDevice();
            services.AddHardwareModule();
            // 3. 构建容器
            ServiceProvider = services.BuildServiceProvider();
            // 自动执行迁移
            ServiceProvider.ApplyMigrations();
            // 4. 启动主窗体
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // 5. 异步设备寻址
            _ = IdentifyDeviceAsync();
        }

        private async Task IdentifyDeviceAsync()
        {
            var deviceService = ServiceProvider.GetRequiredService<IDeviceService>();
            var footerWidget = ServiceProvider.GetRequiredService<FooterWidget>();
            var logService = ServiceProvider.GetRequiredService<ILogService>();

            logService.Info("正在进行设备寻址...");
            var success = await deviceService.IdentifyAsync();

            if (success && deviceService.CurrentDevice is not null)
            {
                footerWidget.SetDeviceCode(deviceService.CurrentDevice.DeviceCode);
                logService.Info($"设备寻址成功：{deviceService.CurrentDevice.DeviceCode}");
            }
            else
            {
                footerWidget.SetDeviceCode("未识别");
                logService.Warn("设备寻址失败，请检查网络或云端设备注册");
            }
        }
    }
}