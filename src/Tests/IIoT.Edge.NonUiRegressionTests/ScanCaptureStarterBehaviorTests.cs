using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.ScanCaptureStarter.Constants;
using IIoT.Edge.Module.ScanCaptureStarter.Integration;
using IIoT.Edge.Module.ScanCaptureStarter.Payload;
using IIoT.Edge.Module.ScanCaptureStarter.Runtime;
using IIoT.Edge.Module.ScanCaptureStarter.Samples;
using IIoT.Edge.Runtime.Signals;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;
using System.Text.Json;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class ScanCaptureStarterBehaviorTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void StarterCellData_WhenRegistered_ShouldRoundTripThroughCellDataRegistry()
    {
        CellDataTypeRegistry.Register<StarterCellData>(StarterModuleConstants.ProcessType);
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var source = new StarterCellData
        {
            Barcode = "STARTER-ROUNDTRIP",
            SequenceNo = 5,
            RuntimeStatus = "Captured",
            DeviceName = "PLC-STARTER",
            DeviceCode = "STARTER-01",
            PlcDeviceId = 7,
            CellResult = true,
            CompletedTime = new DateTime(2026, 4, 21, 9, 0, 0, DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(source, source.GetType(), jsonOptions);
        var restored = Assert.IsType<StarterCellData>(
            CellDataTypeRegistry.Deserialize(StarterModuleConstants.ProcessType, json, jsonOptions));

        Assert.Equal(source.Barcode, restored.Barcode);
        Assert.Equal(source.SequenceNo, restored.SequenceNo);
        Assert.Equal(source.RuntimeStatus, restored.RuntimeStatus);
        Assert.Equal(source.DeviceName, restored.DeviceName);
        Assert.Equal(source.DeviceCode, restored.DeviceCode);
    }

    [Fact]
    public async Task StarterCloudUploader_WhenEnabled_ShouldPostSinglePayloadAndUseIdempotencyKey()
    {
        var logger = new FakeLogService();
        var cloudHttp = new FakeCloudHttpClient();
        var contextStore = new FakeProductionContextStore();
        var configuration = CreateConfiguration(cloudUploadEnabled: true);
        var uploader = new StarterCloudUploader(cloudHttp, logger, configuration, contextStore);
        var deviceSession = new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-STARTER",
            ClientCode = "CLIENT-STARTER",
            ProcessId = Guid.NewGuid()
        };

        var result = await uploader.UploadAsync(
            new ProcessCloudUploadContext(deviceSession),
            [
                new CellCompletedRecord
                {
                    CellData = new StarterCellData
                    {
                        Barcode = "STARTER-CLOUD-01",
                        SequenceNo = 7,
                        RuntimeStatus = "Captured",
                        CellResult = true,
                        DeviceName = deviceSession.DeviceName,
                        DeviceCode = deviceSession.DeviceName,
                        PlcDeviceId = 12,
                        CompletedTime = new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc)
                    }
                }
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Equal("/api/v1/edge/pass-stations/starter-sample", cloudHttp.LastPostUrl);
        Assert.False(string.IsNullOrWhiteSpace(cloudHttp.LastPostOptions?.IdempotencyKey));

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(cloudHttp.LastPayload, WebJsonOptions));
        Assert.Equal(deviceSession.DeviceId, payload.RootElement.GetProperty("deviceId").GetGuid());
        Assert.Equal("STARTER-CLOUD-01", payload.RootElement.GetProperty("item").GetProperty("barcode").GetString());
        Assert.Equal(7, payload.RootElement.GetProperty("item").GetProperty("sequenceNo").GetInt32());
        Assert.Equal("Captured", payload.RootElement.GetProperty("item").GetProperty("runtimeStatus").GetString());
        Assert.Equal("OK", payload.RootElement.GetProperty("item").GetProperty("cellResult").GetString());

        var productionContext = contextStore.GetOrCreate(deviceSession.DeviceName);
        Assert.True(productionContext.Get<bool>(StarterModuleConstants.CloudUploadEnabledKey));
        Assert.Equal(
            StarterModuleConstants.CloudUploadSuccessStatus,
            productionContext.Get<string>(StarterModuleConstants.LastCloudUploadStatusKey));
    }

    [Fact]
    public async Task StarterStationRuntimeFactory_WhenScanAndSequenceArrive_ShouldPublishSingleRecordAndAck()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var services = new ServiceCollection();
        services.AddSingleton<ILogService>(logger);
        services.AddSingleton<IDataPipelineService>(pipeline);
        services.AddSingleton<IBarcodeReaderFactory>(new StubBarcodeReaderFactory(["STARTER-SCAN-0001"]));
        using var provider = services.BuildServiceProvider();

        var buffer = new PlcBuffer(8, 8);
        var context = new ProductionContext
        {
            DeviceName = "PLC-STARTER",
            DeviceId = 9
        };

        var tasks = new StarterStationRuntimeFactory().CreateTasks(provider, buffer, context);

        using var cts = new CancellationTokenSource();
        var runningTasks = tasks.Select(x => x.StartAsync(cts.Token)).ToArray();

        buffer.UpdateReadBuffer([11, 1, 1]);
        await Task.Delay(220);
        buffer.UpdateReadBuffer([10, 1, 1]);
        await Task.Delay(220);

        cts.Cancel();
        await Task.WhenAll(runningTasks);

        var cell = Assert.Single(context.CurrentCells.Values.OfType<StarterCellData>());
        Assert.Equal("STARTER-SCAN-0001", cell.Barcode);
        Assert.Equal(1, cell.SequenceNo);
        Assert.Equal("Captured", cell.RuntimeStatus);
        Assert.True(context.Get<bool>(StarterModuleConstants.RuntimeRegisteredKey));
        Assert.Equal(1, context.Get<int>(StarterModuleConstants.LastPublishedSequenceKey));
        Assert.Equal("STARTER-SCAN-0001", context.Get<string>(StarterModuleConstants.LastPublishedBarcodeKey));
        Assert.False(context.Has(StarterModuleConstants.PendingBarcodeKey));

        Assert.True(pipeline.TryDequeue(out var record));
        Assert.NotNull(record);
        Assert.Equal("STARTER-SCAN-0001", Assert.IsType<StarterCellData>(record!.CellData).Barcode);
        Assert.False(pipeline.TryDequeue(out _));
        Assert.Equal((ushort)1, buffer.GetWriteBuffer()[StarterPlcSignalProfile.AckWriteIndex]);
    }

    [Fact]
    public async Task StarterStationRuntimeFactory_WhenBarcodeReadReturnsEmpty_ShouldNotPublish()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var services = new ServiceCollection();
        services.AddSingleton<ILogService>(logger);
        services.AddSingleton<IDataPipelineService>(pipeline);
        services.AddSingleton<IBarcodeReaderFactory>(new StubBarcodeReaderFactory([]));
        using var provider = services.BuildServiceProvider();

        var buffer = new PlcBuffer(8, 8);
        var context = new ProductionContext
        {
            DeviceName = "PLC-STARTER",
            DeviceId = 9
        };

        var tasks = new StarterStationRuntimeFactory().CreateTasks(provider, buffer, context);

        using var cts = new CancellationTokenSource();
        var runningTasks = tasks.Select(x => x.StartAsync(cts.Token)).ToArray();

        buffer.UpdateReadBuffer([11, 2, 2]);
        await Task.Delay(220);
        buffer.UpdateReadBuffer([10, 2, 2]);
        await Task.Delay(220);

        cts.Cancel();
        await Task.WhenAll(runningTasks);

        Assert.Empty(context.CurrentCells.Values.OfType<StarterCellData>());
        Assert.False(pipeline.TryDequeue(out _));
        Assert.Equal((ushort)0, buffer.GetWriteBuffer()[StarterPlcSignalProfile.AckWriteIndex]);
    }

    [Fact]
    public async Task StarterStationRuntimeFactory_WhenSignalOrderChanges_ShouldStillCaptureAndAckByLabel()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var services = new ServiceCollection();
        services.AddSingleton<ILogService>(logger);
        services.AddSingleton<IDataPipelineService>(pipeline);
        services.AddSingleton<IBarcodeReaderFactory>(new StubBarcodeReaderFactory(["STARTER-SCAN-REORDER"]));
        using var provider = services.BuildServiceProvider();

        var buffer = new PlcBuffer(8, 8);
        var context = new ProductionContext
        {
            DeviceName = "PLC-STARTER",
            DeviceId = 11
        };
        ProductionContextSignalBindings.Set(context,
        [
            new ModuleIoSnapshot("Starter.ResultCode", "DB1.DBW4", 1, "Int16", "Read", 1),
            new ModuleIoSnapshot("Starter.ScanTrigger", "DB1.DBW0", 1, "Int16", "Read", 2),
            new ModuleIoSnapshot("Starter.Sequence", "DB1.DBW2", 1, "Int16", "Read", 3),
            new ModuleIoSnapshot("Starter.Ack", "DB1.DBW8", 1, "Int16", "Write", 1),
            new ModuleIoSnapshot("Starter.ScanResponse", "DB1.DBW6", 1, "Int16", "Write", 2)
        ]);

        var tasks = new StarterStationRuntimeFactory().CreateTasks(provider, buffer, context);

        using var cts = new CancellationTokenSource();
        var runningTasks = tasks.Select(x => x.StartAsync(cts.Token)).ToArray();

        buffer.UpdateReadBuffer([1, 11, 1]);
        await Task.Delay(220);
        buffer.UpdateReadBuffer([1, 10, 1]);
        await Task.Delay(220);

        cts.Cancel();
        await Task.WhenAll(runningTasks);

        var cell = Assert.Single(context.CurrentCells.Values.OfType<StarterCellData>());
        Assert.Equal("STARTER-SCAN-REORDER", cell.Barcode);
        Assert.Equal(1, cell.SequenceNo);
        Assert.Equal((ushort)1, buffer.GetWriteBuffer()[0]);
        Assert.Equal((ushort)10, buffer.GetWriteBuffer()[1]);
    }

    [Fact]
    public async Task ScanCaptureStarterDevelopmentSampleContributor_ShouldSeedDeviceMappingsAndRuntimeSample()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shell:Environment"] = "Development",
                ["Modules:Enabled:0"] = StarterModuleConstants.ModuleId,
                ["DevelopmentSamples:Enabled"] = "true",
                ["DevelopmentSamples:SeedScanCaptureStarterModule"] = "true"
            })
            .Build();
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var contextStore = new FakeProductionContextStore();
        var contributor = new ScanCaptureStarterDevelopmentSampleContributor(
            configuration,
            networkDevices,
            ioMappings,
            contextStore,
            new FakeLogService(),
            [new StarterHardwareProfileProvider()]);

        await contributor.EnsureConfigurationSamplesAsync();
        await contributor.EnsureRuntimeSamplesAsync();

        var device = Assert.Single(networkDevices.Items);
        Assert.Equal(StarterModuleConstants.ModuleId, device.ModuleId);
        Assert.Equal(DeviceType.PLC, device.DeviceType);

        Assert.Equal(5, ioMappings.Items.Count);
        Assert.Equal(
            ["Starter.ScanTrigger", "Starter.Sequence", "Starter.ResultCode", "Starter.ScanResponse", "Starter.Ack"],
            ioMappings.Items.OrderBy(x => x.SortOrder).Select(x => x.Label).ToArray());

        var context = contextStore.GetOrCreate(device.DeviceName);
        var cell = Assert.Single(context.CurrentCells.Values.OfType<StarterCellData>());
        Assert.Equal("STARTER-DEV-0001", cell.Barcode);
        Assert.Equal(1, context.Get<int>(StarterModuleConstants.LastPublishedSequenceKey));
    }

    [Fact]
    public void ScanCaptureStarterModule_ShouldNotBeEnabledInDefaultShellConfig()
    {
        var repoRoot = FindEdgeClientRoot();
        var configPath = Path.Combine(
            repoRoot,
            "src",
            "Edge",
            "IIoT.Edge.Shell",
            "appsettings.json");

        Assert.True(File.Exists(configPath), $"Expected shell config file at '{configPath}'.");

        using var stream = File.OpenRead(configPath);
        using var document = JsonDocument.Parse(stream);
        var enabledModules = document.RootElement
            .GetProperty("Modules")
            .GetProperty("Enabled")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();

        Assert.DoesNotContain(StarterModuleConstants.ModuleId, enabledModules, StringComparer.OrdinalIgnoreCase);
    }

    private static IConfiguration CreateConfiguration(bool cloudUploadEnabled)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:ScanCaptureStarter:CloudUploadEnabled"] = cloudUploadEnabled.ToString()
            })
            .Build();

    private static string FindEdgeClientRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate IIoT.EdgeClient repository root.");
    }

    private sealed class StubBarcodeReaderFactory(IReadOnlyList<string> barcodes) : IBarcodeReaderFactory
    {
        public IBarcodeReader Create(int networkDeviceId, PlcBarcodeReaderOptions options)
            => new StubBarcodeReader(barcodes);
    }

    private sealed class StubBarcodeReader(IReadOnlyList<string> barcodes) : IBarcodeReader
    {
        public Task<string[]> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(barcodes.ToArray());
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
                entity.Id = _nextId++;
            }

            Items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
        {
            Items.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(1);

        public Task<int> ExecuteDeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            var deleted = Items.RemoveAll(x => compiled(x));
            return Task.FromResult(deleted);
        }

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
            => Task.FromResult(Items.FirstOrDefault(x => EqualityComparer<object>.Default.Equals(x.Id, id)));

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
