using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.Runtime.DataPipeline.Tasks;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.TestSimulator.Consumers;
using IIoT.Edge.TestSimulator.Fakes;
using IIoT.Edge.TestSimulator.Scenarios;
using IIoT.Edge.TestSimulator.Services;
using IIoT.Edge.TestSimulator.Tasks;
using IIoT.Edge.TestSimulator.Views;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;
using WpfApplication = System.Windows.Application;

namespace IIoT.Edge.TestSimulator;

public partial class App : WpfApplication
{
    private ServiceProvider? _serviceProvider;
    private readonly CancellationTokenSource _appCts = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        try
        {
            await _serviceProvider.InitializeDapperTablesAsync();

            var backgroundCoordinator = _serviceProvider.GetRequiredService<IBackgroundServiceCoordinator>();
            await backgroundCoordinator.StartAsync(_appCts.Token);

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"模拟器启动失败。\n\n{ex.Message}",
                "IIoT Edge Test Simulator",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _appCts.Cancel();

            if (_serviceProvider is not null)
            {
                var backgroundCoordinator = _serviceProvider.GetRequiredService<IBackgroundServiceCoordinator>();
                using var shutdownCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                backgroundCoordinator.StopAsync(shutdownCts.Token).GetAwaiter().GetResult();
                _serviceProvider.Dispose();
                _serviceProvider = null;
            }
        }
        catch
        {
        }
        finally
        {
            _appCts.Dispose();
            base.OnExit(e);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var testDbDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

        services.AddSingleton<FakeHttpClient>();
        services.AddSingleton<ICloudHttpClient>(sp => sp.GetRequiredService<FakeHttpClient>());

        services.AddSingleton<FakeDeviceService>();
        services.AddSingleton<IDeviceService>(sp => sp.GetRequiredService<FakeDeviceService>());

        services.AddSingleton<FakeLogService>();
        services.AddSingleton<ILogService>(sp => sp.GetRequiredService<FakeLogService>());
        services.AddSingleton<ICriticalPersistenceFallbackWriter, SimulatorCriticalPersistenceFallbackWriter>();

        services.AddSingleton<FakeCloudUploadDiagnosticsStore>();
        services.AddSingleton<ICloudUploadDiagnosticsStore>(sp => sp.GetRequiredService<FakeCloudUploadDiagnosticsStore>());

        services.AddSingleton<FakeMesRetryDiagnosticsStore>();
        services.AddSingleton<IMesRetryDiagnosticsStore>(sp => sp.GetRequiredService<FakeMesRetryDiagnosticsStore>());

        services.AddSingleton<FakeTodayCapacityStore>();
        services.AddSingleton<ITodayCapacityStore>(sp => sp.GetRequiredService<FakeTodayCapacityStore>());

        services.AddSingleton<FakeCapacitySyncTask>();
        services.AddSingleton<ICapacitySyncTask>(sp => sp.GetRequiredService<FakeCapacitySyncTask>());

        services.AddSingleton<FakeDeviceLogSyncTask>();
        services.AddSingleton<IDeviceLogSyncTask>(sp => sp.GetRequiredService<FakeDeviceLogSyncTask>());

        services.AddDapperPersistenceInfrastructure(testDbDir);

        services.AddSingleton(new ShiftConfig
        {
            DayStart = "08:30",
            DayEnd = "20:30"
        });

        services.AddSingleton<SimCapacityConsumer>();
        services.AddSingleton<ICapacityConsumer>(sp => sp.GetRequiredService<SimCapacityConsumer>());
        services.AddSingleton<ICellDataConsumer>(sp => sp.GetRequiredService<ICapacityConsumer>());

        services.AddSingleton<SimCloudConsumer>();
        services.AddSingleton<ICloudConsumer>(sp => sp.GetRequiredService<SimCloudConsumer>());
        services.AddSingleton<ICloudBatchConsumer>(sp => sp.GetRequiredService<SimCloudConsumer>());
        services.AddSingleton<ICellDataConsumer>(sp => sp.GetRequiredService<ICloudConsumer>());

        services.AddSingleton<DataPipelineService>();
        services.AddSingleton<IDataPipelineService>(sp => sp.GetRequiredService<DataPipelineService>());
        services.AddSingleton<IIngressOverflowPersistence, SimulatorIngressOverflowPersistence>();
        services.AddSingleton<ProcessQueueTask>();
        services.AddSingleton<CloudRetryTask>();
        services.AddSingleton<TestRetryTask>(sp => new TestRetryTask(
            sp.GetRequiredService<CloudRetryTask>()));

        services.AddSingleton<IBackgroundServiceCoordinator, BackgroundServiceCoordinator>();
        services.AddSingleton<IManagedBackgroundService>(sp =>
            new LongRunningBackgroundTaskGroupService(
                "Simulator.DataPipeline",
                new IBackgroundTask[] { sp.GetRequiredService<ProcessQueueTask>() }));

        services.AddSingleton<SimDataHelper>();

        services.AddSingleton<OnlinePassScenario>();
        services.AddSingleton<OfflineBufferScenario>();
        services.AddSingleton<RetryScenario>();
        services.AddSingleton<HistoricalDataScenario>();
        services.AddSingleton<ScenarioRunner>();

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
