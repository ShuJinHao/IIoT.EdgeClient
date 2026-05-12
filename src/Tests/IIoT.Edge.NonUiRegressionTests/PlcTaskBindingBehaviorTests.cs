using System.Linq.Expressions;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Factory;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Homogenization;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Runtime.Plc;
using IIoT.Edge.Runtime.Signals;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class PlcTaskBindingBehaviorTests
{
    [Fact]
    public async Task GetEnabledTaskKeys_WhenDefaultEnabled_ShouldEnableMissingRows()
    {
        var service = CreateService(defaultEnableAllTasks: true);

        var enabledKeys = await service.Service.GetEnabledTaskKeysAsync(1, TestCandidates, AllTestMappings);

        Assert.Equal(["Task.A", "Task.B"], enabledKeys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetEnabledTaskKeys_WhenProductionWithoutConfiguredDefault_ShouldEnableMissingRows()
    {
        var service = CreateService(defaultEnableAllTasks: null, environmentName: "Production");

        var enabledKeys = await service.Service.GetEnabledTaskKeysAsync(1, TestCandidates, AllTestMappings);

        Assert.Equal(["Task.A", "Task.B"], enabledKeys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetEnabledTaskKeys_WhenDevelopmentWithoutConfiguredDefault_ShouldDisableMissingRows()
    {
        var service = CreateService(defaultEnableAllTasks: null, environmentName: "Development");

        var enabledKeys = await service.Service.GetEnabledTaskKeysAsync(1, TestCandidates, AllTestMappings);

        Assert.Empty(enabledKeys);
    }

    [Theory]
    [InlineData("Production", false, 0)]
    [InlineData("Development", true, 2)]
    public async Task GetEnabledTaskKeys_WhenConfiguredDefaultExists_ShouldOverrideEnvironment(
        string environmentName,
        bool configuredDefault,
        int expectedEnabledCount)
    {
        var service = CreateService(
            defaultEnableAllTasks: configuredDefault,
            environmentName: environmentName);

        var enabledKeys = await service.Service.GetEnabledTaskKeysAsync(1, TestCandidates, AllTestMappings);

        Assert.Equal(expectedEnabledCount, enabledKeys.Count);
    }

    [Fact]
    public async Task GetEnabledTaskKeys_WhenSavedRowExists_ShouldOverrideDefault()
    {
        var harness = CreateService(defaultEnableAllTasks: true);
        harness.Bindings.Add(PlcTaskBindingEntity.Create(1, "Task.A", enabled: false, DateTimeOffset.UtcNow));

        var enabledKeys = await harness.Service.GetEnabledTaskKeysAsync(1, TestCandidates, AllTestMappings);

        Assert.Equal(["Task.B"], enabledKeys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetEnabledTaskKeys_WhenDefaultEnabled_ShouldOnlyEnableRunnableTasks()
    {
        var service = CreateService(defaultEnableAllTasks: true);
        var mappings = AllTestMappings
            .Where(static mapping => !string.Equals(mapping.SignalKey, "Signal.Business", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var enabledKeys = await service.Service.GetEnabledTaskKeysAsync(1, TestCandidates, mappings);

        Assert.Equal(["Task.A"], enabledKeys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateEnabledTasks_WhenWriteSignalMissing_ShouldReportDirectionSpecificIssue()
    {
        var service = CreateService(defaultEnableAllTasks: true).Service;
        var mappings = new[]
        {
            new ModuleIoSnapshot("Signal.Shared", "D100", 1, "Int16", "Read", 1, "信号交互", "共享信号", "读点")
        };

        var result = service.ValidateEnabledTasks(
            TestCandidates,
            new HashSet<string>(["Task.A"], StringComparer.OrdinalIgnoreCase),
            mappings);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("Task.A", issue.TaskKey);
        Assert.Equal("Signal.Shared", issue.RequiredSignal!.SignalKey);
        Assert.Equal("Write", issue.RequiredSignal.Direction);
    }

    [Fact]
    public async Task SaveDeviceBindingsAsync_WhenHeartbeatDisabled_ShouldPersistRowsAndWriteWarning()
    {
        var harness = CreateService(defaultEnableAllTasks: true);
        var device = NetworkDeviceEntity.Create("PLC-A", DeviceType.PLC, "127.0.0.1", 102);
        device.AssignModule("TestModule", PlcType.S7.ToString());
        harness.NetworkDevices.Add(device);

        await harness.Service.SaveDeviceBindingsAsync(
            device.Id,
            "TestModule",
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["Task.A"] = false,
                ["Task.B"] = true
            });

        Assert.Equal(2, harness.Bindings.Items.Count);
        Assert.Contains(harness.Bindings.Items, static x => x.TaskKey == "Task.A" && !x.Enabled);
        Assert.Contains(harness.Bindings.Items, static x => x.TaskKey == "Task.B" && x.Enabled);
        Assert.Contains(harness.Logger.Warnings, static message => message.Contains("心跳", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveDeviceBindingsAsync_WhenEnabledTaskMissingIo_ShouldFail()
    {
        var harness = CreateService(defaultEnableAllTasks: false, seedIoMappings: false);
        var device = NetworkDeviceEntity.Create("PLC-A", DeviceType.PLC, "127.0.0.1", 102);
        device.AssignModule("TestModule", PlcType.S7.ToString());
        harness.NetworkDevices.Add(device);
        AddTestIoMappings(harness.IoMappings, device.Id, includeBusinessSignal: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.SaveDeviceBindingsAsync(
            device.Id,
            "TestModule",
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["Task.A"] = false,
                ["Task.B"] = true
            }));

        Assert.Contains("Signal.Business/Read", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HomogenizationStationRuntimeFactory_WhenOnlyHeartbeatEnabled_ShouldCreateOnlyHeartbeatTask()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction>>(HomogenizationSignalTestProfile.InteractionProfileInstance);
        services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead>>(HomogenizationSignalTestProfile.SingleReadProfileInstance);
        services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead>>(HomogenizationSignalTestProfile.ContinuousReadProfileInstance);
        services.AddSingleton<ILogService, FakeLogService>();
        services.AddSingleton<IProductionTimeProvider, FakeProductionTimeProvider>();
        services.AddSingleton<IProductionContextSignalBindingStore, ProductionContextSignalBindingStore>();
        services.AddSingleton(Options.Create(new HomogenizationModuleOptions()));
        services.AddSingleton(Options.Create(new HomogenizationCodeOptions()));
        using var provider = services.BuildServiceProvider();
        var factory = new HomogenizationStationRuntimeFactory();
        var buffer = new PlcBuffer(512, 64);
        var context = new HomogenizationContext { DeviceName = "PLC-H", NetworkDeviceId = 1 };

        var tasks = factory.CreateTasks(
            provider,
            buffer,
            context,
            new HashSet<string>([HomogenizationTaskKeys.Heartbeat], StringComparer.OrdinalIgnoreCase));

        var task = Assert.Single(tasks);
        Assert.Equal(HomogenizationTaskKeys.Heartbeat, task.TaskName);
    }

    [Fact]
    public async Task PlcLifecycleCoordinator_WhenOneDeviceHasRuntimeFault_ShouldSkipOnlyBlockedDevice()
    {
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var blockedDevice = networkDevices.Add(CreateLifecyclePlc("PLC-A", 6100));
        var healthyDevice = networkDevices.Add(CreateLifecyclePlc("PLC-B", 6101));
        var contextStore = new FakeProductionContextStore();
        var logger = new FakeLogService();
        var runtimeRegistry = new PlcRuntimeRegistry();
        var statusStore = new PlcConnectionStatusStore();
        var plcServiceFactory = new TrackingPlcServiceFactory();
        var runtimeBuilder = new PlcDeviceRuntimeBuilder(
            ioMappings,
            new PlcDataStore(),
            plcServiceFactory,
            contextStore,
            logger,
            statusStore,
            new DefaultPlcSignalBlockPlanner(),
            []);
        var coordinator = new PlcLifecycleCoordinator(
            networkDevices,
            contextStore,
            logger,
            runtimeRegistry,
            runtimeBuilder,
            statusStore);
        var bindingFault = "PLC“PLC-A”任务绑定校验失败：业务(Task.A) 缺少 Signal.Shared/Write。";
        runtimeRegistry.BlockRuntime(blockedDevice.DeviceName);
        statusStore.MarkRuntimeFault(blockedDevice.Id, blockedDevice.DeviceName, bindingFault);

        await coordinator.InitializeAsync();

        try
        {
            Assert.DoesNotContain("PLC-A", plcServiceFactory.CreatedDeviceNames);
            Assert.Contains("PLC-B", plcServiceFactory.CreatedDeviceNames);
            var blockedSnapshot = statusStore.GetSnapshot(blockedDevice.Id);
            Assert.NotNull(blockedSnapshot);
            Assert.False(blockedSnapshot!.IsConnected);
            Assert.Equal(bindingFault, blockedSnapshot.LastError);
            Assert.True(statusStore.GetSnapshot(healthyDevice.Id)?.IsConnected);
        }
        finally
        {
            await coordinator.StopAsync();
        }
    }

    private static readonly IReadOnlyCollection<TaskCandidate> TestCandidates =
    [
        new(
            "Task.A",
            "心跳",
            [
                new TaskRequiredSignal("Signal.Shared", "Read"),
                new TaskRequiredSignal("Signal.Shared", "Write")
            ],
            IsHeartbeatLike: true),
        new(
            "Task.B",
            "业务",
            [new TaskRequiredSignal("Signal.Business", "Read")])
    ];

    private static readonly IReadOnlyCollection<ModuleIoSnapshot> AllTestMappings =
    [
        new("Signal.Shared", "D100", 1, "Int16", "Read", 1, "信号交互", "共享信号", "读点"),
        new("Signal.Shared", "D200", 1, "Int16", "Write", 2, "信号交互", "共享信号", "写点"),
        new("Signal.Business", "D300", 1, "Int16", "Read", 3, "单点读数据", "业务信号", "读点")
    ];

    private static BindingServiceHarness CreateService(
        bool? defaultEnableAllTasks,
        string environmentName = "Production",
        bool seedIoMappings = true)
    {
        var settings = new Dictionary<string, string?>();
        if (defaultEnableAllTasks.HasValue)
        {
            settings["PlcTaskBinding:DefaultEnableAllTasks"] = defaultEnableAllTasks.Value.ToString();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var runtimeRegistry = new FakeStationRuntimeRegistry(new FakeStationRuntimeFactory());
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var bindings = new InMemoryRepository<PlcTaskBindingEntity>();
        var logger = new FakeLogService();
        if (seedIoMappings)
        {
            AddTestIoMappings(ioMappings, networkDeviceId: 1);
        }

        var service = new PlcTaskBindingService(
            configuration,
            new FakeHostEnvironment(environmentName),
            runtimeRegistry,
            networkDevices,
            ioMappings,
            bindings,
            logger);

        return new BindingServiceHarness(service, networkDevices, ioMappings, bindings, logger);
    }

    private static void AddTestIoMappings(
        InMemoryRepository<IoMappingEntity> ioMappings,
        int networkDeviceId,
        bool includeBusinessSignal = true)
    {
        ioMappings.Add(IoMappingEntity.Create(
            networkDeviceId,
            "Signal.Shared",
            "D100",
            1,
            "Int16",
            "Read",
            "信号交互",
            "共享信号",
            "读点"));
        ioMappings.Add(IoMappingEntity.Create(
            networkDeviceId,
            "Signal.Shared",
            "D200",
            1,
            "Int16",
            "Write",
            "信号交互",
            "共享信号",
            "写点"));
        if (!includeBusinessSignal)
        {
            return;
        }

        ioMappings.Add(IoMappingEntity.Create(
            networkDeviceId,
            "Signal.Business",
            "D300",
            1,
            "Int16",
            "Read",
            "单点读数据",
            "业务信号",
            "读点"));
    }

    private static NetworkDeviceEntity CreateLifecyclePlc(string deviceName, int port)
    {
        var device = NetworkDeviceEntity.Create(deviceName, DeviceType.PLC, "127.0.0.1", port);
        device.AssignModule("TestModule", PlcType.S7.ToString());
        return device;
    }

    private sealed record BindingServiceHarness(
        IPlcTaskBindingService Service,
        InMemoryRepository<NetworkDeviceEntity> NetworkDevices,
        InMemoryRepository<IoMappingEntity> IoMappings,
        InMemoryRepository<PlcTaskBindingEntity> Bindings,
        FakeLogService Logger);

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "IIoT.Edge.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeStationRuntimeRegistry(IStationRuntimeFactory factory) : IStationRuntimeRegistry
    {
        public void Register(IStationRuntimeFactory runtimeFactory)
        {
        }

        public bool HasFactory(string moduleId)
            => string.Equals(moduleId, factory.ModuleId, StringComparison.OrdinalIgnoreCase);

        public bool TryGetFactory(string moduleId, out IStationRuntimeFactory runtimeFactory)
        {
            if (HasFactory(moduleId))
            {
                runtimeFactory = factory;
                return true;
            }

            runtimeFactory = null!;
            return false;
        }

        public IReadOnlyDictionary<string, IStationRuntimeFactory> GetRegistrations()
            => new Dictionary<string, IStationRuntimeFactory>(StringComparer.OrdinalIgnoreCase)
            {
                [factory.ModuleId] = factory
            };
    }

    private sealed class FakeStationRuntimeFactory : IStationRuntimeFactory
    {
        public string ModuleId => "TestModule";

        public IReadOnlyCollection<TaskCandidate> GetTaskCandidates()
            => TestCandidates;

        public List<IPlcTask> CreateTasks(
            IServiceProvider serviceProvider,
            IPlcBuffer buffer,
            ProductionContext context,
            IReadOnlySet<string> enabledTaskKeys)
            => [];
    }

    private sealed class FakeLogService : ILogService
    {
        public List<string> Warnings { get; } = [];

        public event Action<LogEntry> EntryAdded
        {
            add { }
            remove { }
        }

        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message) { }
        public void Fatal(string message) { }
    }

    private sealed class FakeProductionTimeProvider : IProductionTimeProvider
    {
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Local;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime BusinessNow => DateTime.Now;
        public DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        public DateTime ToBusinessTime(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
        public string FormatBusinessTimestamp(DateTime value) => ToBusinessTime(value).ToString("yyyy-MM-dd HH:mm:ss");
    }

    private sealed class TrackingPlcServiceFactory : IPlcServiceFactory
    {
        public List<string> CreatedDeviceNames { get; } = [];

        public IPlcService Create(PlcType plcType, string deviceName)
        {
            CreatedDeviceNames.Add(deviceName);
            return new ConnectedPlcService();
        }
    }

    private sealed class ConnectedPlcService : IPlcService
    {
        public bool IsConnected { get; private set; }

        public void Init(string ip, int port)
        {
        }

        public Task<bool> ConnectAsync()
        {
            IsConnected = true;
            return Task.FromResult(true);
        }

        public void Disconnect()
            => IsConnected = false;

        public Task<List<T>> ReadDataAsync<T>(string address, ushort length)
            => Task.FromResult(Enumerable.Repeat(default(T)!, length).ToList());

        public Task WriteDataAsync<T>(string address, List<T> data)
            => Task.CompletedTask;

        public void Dispose()
            => Disconnect();
    }

    private sealed class InMemoryRepository<T> : IRepository<T>
        where T : class, IEntity<int>, IAggregateRoot
    {
        private int _nextId = 1;

        public List<T> Items { get; } = [];

        public IQueryable<T> GetQueryable() => Items.AsQueryable();

        public T Add(T entity)
        {
            if (entity.Id == 0)
            {
                EntityIdTestHelper.SetId(entity, _nextId++);
            }

            Items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
            => Items.Remove(entity);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(1);

        public Task<int> ExecuteDeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            return Task.FromResult(Items.RemoveAll(item => compiled(item)));
        }

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
            => Task.FromResult(Items.FirstOrDefault(item => EqualityComparer<object>.Default.Equals(item.Id, id)));

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(expression.Compile()));

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Where(expression.Compile()).ToList());

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Where(expression.Compile()).ToList());

        public Task<List<T>> GetListAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> GetSingleOrDefaultAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetCountAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Count(expression.Compile()));

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
