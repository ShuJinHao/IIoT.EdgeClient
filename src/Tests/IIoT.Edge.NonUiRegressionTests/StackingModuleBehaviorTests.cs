using AutoMapper;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Integration;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.Module.Stacking.Runtime;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class StackingModuleBehaviorTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void StackingCellData_WhenRegistered_ShouldRoundTripThroughCellDataRegistry()
    {
        CellDataTypeRegistry.Register<StackingCellData>(StackingModuleConstants.ProcessType);
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var source = new StackingCellData
        {
            Barcode = "ST-ROUNDTRIP",
            TrayCode = "TRAY-01",
            LayerCount = 12,
            SequenceNo = 5,
            RuntimeStatus = "DevelopmentSample",
            DeviceName = "PLC-S",
            DeviceCode = "STACK-01",
            CompletedTime = new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(source, source.GetType(), jsonOptions);
        var restored = Assert.IsType<StackingCellData>(
            CellDataTypeRegistry.Deserialize(StackingModuleConstants.ProcessType, json, jsonOptions));

        Assert.Equal(StackingModuleConstants.ProcessType, restored.ProcessType);
        Assert.Equal(source.Barcode, restored.Barcode);
        Assert.Equal(source.TrayCode, restored.TrayCode);
        Assert.Equal(source.LayerCount, restored.LayerCount);
        Assert.Equal(source.SequenceNo, restored.SequenceNo);
        Assert.Equal(source.RuntimeStatus, restored.RuntimeStatus);
        Assert.Equal(source.DeviceName, restored.DeviceName);
        Assert.Equal(source.DeviceCode, restored.DeviceCode);
    }

    [Fact]
    public async Task StackingCloudUploader_WhenEnabled_ShouldPostSinglePayloadAndUpdateDiagnostics()
    {
        var logger = new FakeLogService();
        var cloudHttp = new FakeCloudHttpClient();
        var contextStore = new FakeProductionContextStore();
        var configuration = CreateConfiguration(cloudUploadEnabled: true);
        var uploader = new StackingCloudUploader(
            cloudHttp,
            CreateMapper(),
            logger,
            configuration,
            contextStore);
        var deviceSession = new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-S",
            ClientCode = "CLIENT-STACK",
            ProcessId = Guid.NewGuid()
        };

        var result = await uploader.UploadAsync(
            new ProcessCloudUploadContext(deviceSession),
            [
                new CellCompletedRecord
                {
                    CellData = new StackingCellData
                    {
                        Barcode = "ST-CLOUD-01",
                        TrayCode = "TRAY-CLOUD-01",
                        LayerCount = 11,
                        SequenceNo = 7,
                        CellResult = true,
                        DeviceName = deviceSession.DeviceName,
                        CompletedTime = new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc)
                    }
                }
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Equal("/api/v1/edge/pass-stations/stacking", cloudHttp.LastPostUrl);

        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(cloudHttp.LastPayload, WebJsonOptions));
        Assert.Equal(deviceSession.DeviceId, payload.RootElement.GetProperty("deviceId").GetGuid());
        Assert.Equal("ST-CLOUD-01", payload.RootElement.GetProperty("item").GetProperty("barcode").GetString());
        Assert.Equal("TRAY-CLOUD-01", payload.RootElement.GetProperty("item").GetProperty("trayCode").GetString());
        Assert.Equal(11, payload.RootElement.GetProperty("item").GetProperty("layerCount").GetInt32());
        Assert.Equal(7, payload.RootElement.GetProperty("item").GetProperty("sequenceNo").GetInt32());
        Assert.Equal("OK", payload.RootElement.GetProperty("item").GetProperty("cellResult").GetString());

        var productionContext = contextStore.GetOrCreate(deviceSession.DeviceName);
        Assert.True(productionContext.Get<bool>(StackingModuleConstants.CloudUploadEnabledKey));
        Assert.Equal(
            StackingModuleConstants.CloudUploadSuccessStatus,
            productionContext.Get<string>(StackingModuleConstants.LastCloudUploadStatusKey));
        Assert.NotEqual(
            default,
            productionContext.Get<DateTime>(StackingModuleConstants.LastCloudUploadAtKey));
        Assert.Null(productionContext.Get<string>(StackingModuleConstants.LastCloudUploadErrorKey));
    }

    [Fact]
    public async Task StackingCloudUploader_WhenDisabled_ShouldReturnFalseAndRecordDisabledDiagnostic()
    {
        var logger = new FakeLogService();
        var cloudHttp = new FakeCloudHttpClient();
        var contextStore = new FakeProductionContextStore();
        var uploader = new StackingCloudUploader(
            cloudHttp,
            CreateMapper(),
            logger,
            CreateConfiguration(cloudUploadEnabled: false),
            contextStore);
        var deviceSession = new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-S",
            ClientCode = "CLIENT-STACK",
            ProcessId = Guid.NewGuid()
        };

        var result = await uploader.UploadAsync(
            new ProcessCloudUploadContext(deviceSession),
            [
                new CellCompletedRecord
                {
                    CellData = new StackingCellData
                    {
                        Barcode = "ST-CLOUD-DISABLED",
                        DeviceName = deviceSession.DeviceName
                    }
                }
            ]);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Contains(logger.Entries, x => x.Message.Contains("Stacking cloud upload is disabled", StringComparison.Ordinal));

        var productionContext = contextStore.GetOrCreate(deviceSession.DeviceName);
        Assert.False(productionContext.Get<bool>(StackingModuleConstants.CloudUploadEnabledKey));
        Assert.Equal(
            StackingModuleConstants.CloudUploadDisabledStatus,
            productionContext.Get<string>(StackingModuleConstants.LastCloudUploadStatusKey));
        Assert.Equal(
            "Stacking cloud upload is disabled by configuration.",
            productionContext.Get<string>(StackingModuleConstants.LastCloudUploadErrorKey));
    }

    [Fact]
    public async Task StackingCloudUploader_WhenCellResultVariantsAreUploaded_ShouldMapUnknownOkAndNg()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var uploader = new StackingCloudUploader(
            cloudHttp,
            CreateMapper(),
            new FakeLogService(),
            CreateConfiguration(cloudUploadEnabled: true),
            new FakeProductionContextStore());
        var deviceSession = new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-S",
            ClientCode = "CLIENT-STACK",
            ProcessId = Guid.NewGuid()
        };

        await uploader.UploadAsync(
            new ProcessCloudUploadContext(deviceSession),
            [
                CreateStackingRecord(deviceSession.DeviceName, "ST-UNKNOWN", null, 1),
                CreateStackingRecord(deviceSession.DeviceName, "ST-OK", true, 2),
                CreateStackingRecord(deviceSession.DeviceName, "ST-NG", false, 3)
            ]);

        Assert.Equal(3, cloudHttp.PostPayloads.Count);
        Assert.Equal("Unknown", ReadPayloadCellResult(cloudHttp.PostPayloads[0]));
        Assert.Equal("OK", ReadPayloadCellResult(cloudHttp.PostPayloads[1]));
        Assert.Equal("NG", ReadPayloadCellResult(cloudHttp.PostPayloads[2]));
    }

    [Fact]
    public async Task StackingSignalCaptureTask_WhenSequenceArrives_ShouldPopulateContextAndPipeline()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var buffer = new PlcBuffer(8, 8);
        var context = new ProductionContext
        {
            DeviceName = "PLC-STACKING-DEV",
            DeviceId = 7
        };

        var task = new StackingSignalCaptureTask(buffer, context, pipeline, logger);
        buffer.UpdateReadBuffer(new ushort[] { 3, 16, 1 });

        using var cts = new CancellationTokenSource();
        var runTask = task.StartAsync(cts.Token);
        await Task.Delay(160);
        cts.Cancel();
        await runTask;

        var cell = Assert.Single(context.CurrentCells.Values.OfType<StackingCellData>());
        Assert.Equal("PLC-STACKING-DEV-ST-0003", cell.Barcode);
        Assert.Equal(16, cell.LayerCount);
        Assert.Equal(3, cell.SequenceNo);
        Assert.Equal("Captured", cell.RuntimeStatus);
        Assert.True(context.Get<bool>(StackingModuleConstants.RuntimeRegisteredKey));
        Assert.Equal(3, context.Get<int>(StackingModuleConstants.LastPublishedSequenceKey));
        Assert.Equal(cell.Barcode, context.Get<string>(StackingModuleConstants.LastPublishedBarcodeKey));

        Assert.True(pipeline.TryDequeue(out var record));
        Assert.NotNull(record);
        Assert.Equal(cell.Barcode, Assert.IsType<StackingCellData>(record!.CellData).Barcode);
    }

    [Fact]
    public async Task StackingSignalCaptureTask_WhenResultCodeIsUnknown_ShouldKeepCellResultNull()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var buffer = new PlcBuffer(8, 8);
        var context = new ProductionContext
        {
            DeviceName = "PLC-STACKING-DEV",
            DeviceId = 8
        };

        var task = new StackingSignalCaptureTask(buffer, context, pipeline, logger);
        buffer.UpdateReadBuffer(new ushort[] { 4, 18, 0 });

        using var cts = new CancellationTokenSource();
        var runTask = task.StartAsync(cts.Token);
        await Task.Delay(160);
        cts.Cancel();
        await runTask;

        var cell = Assert.Single(context.CurrentCells.Values.OfType<StackingCellData>());
        Assert.Null(cell.CellResult);
    }

    private static CellCompletedRecord CreateStackingRecord(
        string deviceName,
        string barcode,
        bool? cellResult,
        int sequenceNo)
        => new()
        {
            CellData = new StackingCellData
            {
                Barcode = barcode,
                TrayCode = "TRAY-TEST",
                LayerCount = 8,
                SequenceNo = sequenceNo,
                CellResult = cellResult,
                DeviceName = deviceName,
                CompletedTime = new DateTime(2026, 4, 16, 12, 0, sequenceNo, DateTimeKind.Utc)
            }
        };

    private static string ReadPayloadCellResult(object payload)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, WebJsonOptions));
        return document.RootElement.GetProperty("item").GetProperty("cellResult").GetString()
               ?? string.Empty;
    }

    private static IConfiguration CreateConfiguration(bool cloudUploadEnabled)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:Stacking:CloudUploadEnabled"] = cloudUploadEnabled.ToString()
            })
            .Build();

    private static IMapper CreateMapper()
        => new MapperConfiguration(
                cfg => cfg.AddProfile<StackingCloudProfile>(),
                NullLoggerFactory.Instance)
            .CreateMapper();
}
