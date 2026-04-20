using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.ContractTests;

public abstract class ModuleContractTestBase<TModule>
    where TModule : IEdgeStationModule, new()
{
    private readonly ModuleContractFixture _fixture = new();

    protected virtual bool RequiresHardwareProfile => false;
    protected virtual bool RequiresMesUploader => false;
    protected virtual int ExpectedRuntimeTaskCount => 0;
    protected virtual int MinimumRouteCount => 1;

    protected TModule CreateModule() => new();

    protected virtual void ConfigureRuntimeServices(IServiceCollection services)
    {
    }

    [Fact]
    public void RegisterPipeline_ShouldPopulateRequiredModuleContracts()
    {
        var module = CreateModule();
        var result = _fixture.RegisterModule(module);

        Assert.True(result.CellDataRegistry.IsRegistered(module.ProcessType));
        Assert.True(result.RuntimeRegistry.HasFactory(module.ModuleId));
        Assert.True(result.IntegrationRegistry.HasCloudUploader(module.ProcessType));
        Assert.Equal(
            RequiresMesUploader,
            result.IntegrationRegistry.HasMesUploader(module.ProcessType));

        var moduleRoutes = result.ViewRegistry.GetAllViewRegistrations()
            .Where(x => x.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal))
            .ToArray();
        Assert.True(moduleRoutes.Length >= MinimumRouteCount,
            $"Module '{module.ModuleId}' should register at least {MinimumRouteCount} route(s).");

        var moduleMenus = result.ViewRegistry.GetAllMenus()
            .Where(x => x.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(moduleMenus);
    }

    [Fact]
    public void RegisterServices_ShouldRegisterCloudUploaderAndOptionalHardwareProfile()
    {
        var module = CreateModule();
        var result = _fixture.RegisterModule(module);

        var cloudUploaderDescriptors = result.Services
            .Where(static x => x.ServiceType == typeof(IProcessCloudUploader))
            .ToArray();
        Assert.NotEmpty(cloudUploaderDescriptors);

        var hardwareProfileDescriptors = result.Services
            .Where(static x => x.ServiceType == typeof(IModuleHardwareProfileProvider))
            .ToArray();

        if (RequiresHardwareProfile)
        {
            Assert.NotEmpty(hardwareProfileDescriptors);
        }
        else
        {
            Assert.Empty(hardwareProfileDescriptors);
        }

        var mesUploaderDescriptors = result.Services
            .Where(static x => x.ServiceType == typeof(IProcessMesUploader))
            .ToArray();

        if (RequiresMesUploader)
        {
            Assert.NotEmpty(mesUploaderDescriptors);
        }
    }

    [Fact]
    public void RegisterViews_ShouldKeepModuleRoutesOutOfCoreNamespace()
    {
        var module = CreateModule();
        var result = _fixture.RegisterModule(module);

        Assert.All(
            result.ViewRegistry.GetAllViewRegistrations()
                .Where(x => x.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal)),
            view => Assert.True(
                view.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal),
                $"View '{view.ViewId}' must use the '{module.ModuleId}.' prefix."));

        Assert.All(
            result.ViewRegistry.GetAllMenus()
                .Where(x => x.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal)),
            menu => Assert.True(
                menu.ViewId.StartsWith($"{module.ModuleId}.", StringComparison.Ordinal),
                $"Menu view id '{menu.ViewId}' must use the '{module.ModuleId}.' prefix."));
    }

    [Fact]
    public void RuntimeFactory_ShouldCreateTasksWithMinimalRuntimeServices()
    {
        var module = CreateModule();
        var result = _fixture.RegisterModule(module);
        Assert.True(result.RuntimeRegistry.TryGetFactory(module.ModuleId, out var factory));

        var services = new ServiceCollection();
        ConfigureRuntimeServices(services);

        var tasks = factory.CreateTasks(
            services.BuildServiceProvider(),
            new PlcBuffer(16, 16),
            new ProductionContext { DeviceName = "PLC-A" });

        Assert.Equal(ExpectedRuntimeTaskCount, tasks.Count);
    }

    protected static void AddDefaultRuntimeServices(IServiceCollection services)
    {
        services.AddSingleton<ILogService, ContractLogService>();
        services.AddSingleton<IDataPipelineService, ContractDataPipelineService>();
    }

    private sealed class ContractLogService : ILogService
    {
        public event Action<LogEntry>? EntryAdded;

        public void Debug(string message) => Raise(message);
        public void Info(string message) => Raise(message);
        public void Warn(string message) => Raise(message);
        public void Error(string message) => Raise(message);
        public void Fatal(string message) => Raise(message);

        private void Raise(string message)
        {
            EntryAdded?.Invoke(new LogEntry
            {
                Level = "Test",
                Message = message,
                Time = DateTime.UtcNow
            });
        }
    }

    private sealed class ContractDataPipelineService : IDataPipelineService
    {
        public int PendingCount => 0;
        public int OverflowCount => 0;
        public int SpillCount => 0;

        public ValueTask<DataPipelineEnqueueResult> EnqueueAsync(
            CellCompletedRecord record,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(DataPipelineEnqueueResult.Accepted());

        public bool TryDequeue(out CellCompletedRecord? record)
        {
            record = null;
            return false;
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);
    }
}
