// 路径：src/Edge/IIoT.Edge.Shell/App.xaml.cs
using IIoT.Edge.CloudSync;
using IIoT.Edge.Infrastructure;
using IIoT.Edge.PlcDevice;
using IIoT.Edge.Module.Hardware;
using IIoT.Edge.Module.Production;
using IIoT.Edge.Module.Config;
using IIoT.Edge.Module.Formula;
using IIoT.Edge.Module.SysLog;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Tasks.Context;
using IIoT.Edge.UI.Shared;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.Module.Hardware.Plc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;

namespace IIoT.Edge.Shell
{
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; private set; } = null!;

        private CancellationTokenSource _appCts = new();

        public App()
        {
            _ = typeof(MaterialDesignThemes.Wpf.BundledTheme).Assembly;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var services = new ServiceCollection();
            var viewRegistry = new ViewRegistry();

            // 1. 模块扫描
            var machineModule = configuration["MachineModule"];
            IModuleLoader loader = new ModuleLoader(services, viewRegistry);
            loader.LoadFromDirectory(
                AppDomain.CurrentDomain.BaseDirectory, machineModule);

            // 2. 各层 DI — 每层自己的扩展方法，App只调不写
            var dbPath = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "IIoT.Edge", "edge.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            services.AddInfrastructure(dbPath);
            services.AddCloudSync(configuration);
            services.AddPlcDevice();
            services.AddShellWidgets();
            services.AddShell(viewRegistry);
            services.AddHardwareModule();
            services.AddProductionModule();
            services.AddConfigModule();
            services.AddFormulaModule();
            services.AddSysLogModule();

            // 3. 构建容器
            ServiceProvider = services.BuildServiceProvider();
            ServiceProvider.ApplyMigrations();

            // 4. 恢复生产上下文
            var contextStore = ServiceProvider.GetRequiredService<ProductionContextStore>();
            contextStore.LoadFromFile();

            // 5. 启动自动保存
            _ = contextStore.StartAutoSaveAsync(_appCts.Token, intervalSeconds: 30);

            // 6. 启动主窗体
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // 7. 异步设备寻址
            _ = IdentifyDeviceAsync();

            // 8. PLC任务初始化（注册逻辑在DependencyInjection里）
            _ = ServiceProvider.InitializePlcTasksAsync(_appCts.Token);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            var contextStore = ServiceProvider.GetRequiredService<ProductionContextStore>();
            contextStore.SaveToFile();

            _appCts.Cancel();

            var plcManager = ServiceProvider.GetRequiredService<PlcConnectionManager>();
            plcManager.Dispose();

            base.OnExit(e);
        }

        private async Task IdentifyDeviceAsync()
        {
            var deviceService = ServiceProvider
                .GetRequiredService<IIoT.Edge.Contracts.Device.IDeviceService>();
            var footerWidget = ServiceProvider
                .GetRequiredService<IIoT.Edge.UI.Shared.Widgets.Footer.FooterWidget>();
            var logService = ServiceProvider
                .GetRequiredService<IIoT.Edge.Contracts.ILogService>();

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
                logService.Warn("设备寻址失败");
            }
        }
    }
}