using System.Text.Json;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Homogenization;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationCloudUploaderBehaviorTests
{
    [Fact]
    public void HomogenizationCloudCodeOptions_ShouldUseConfiguredStatusLevelMapping()
    {
        var options = new HomogenizationCloudCodeOptions
        {
            EquipmentStatusLevels =
            {
                ["7"] = "ERROR"
            }
        };

        var normalLevel = options.ResolveEquipmentStatusLevel(new HomogenizationEquipmentStatusSnapshot
        {
            StatusCode = 1,
            StatusText = "空闲"
        });
        var configuredLevel = options.ResolveEquipmentStatusLevel(new HomogenizationEquipmentStatusSnapshot
        {
            StatusCode = 7,
            StatusText = "空闲"
        });

        Assert.Equal("INFO", normalLevel);
        Assert.Equal("ERROR", configuredLevel);
    }

    [Fact]
    public async Task HomogenizationCloudUploader_ShouldUploadProcessPayloadWithCloudDeviceId()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var logger = new FakeLogService();
        var uploader = new HomogenizationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp, logger);
        var completedTime = new DateTime(2026, 4, 30, 8, 0, 0, DateTimeKind.Utc);
        var device = new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-H",
            ClientCode = "CLIENT-H"
        };

        var result = await uploader.UploadAsync(
            new ProcessCloudUploadContext(device),
            [
                new CellCompletedRecord
                {
                    CellData = new HomogenizationCellData
                    {
                        TrayCode = "TRAY-H-001",
                        DeviceName = device.DeviceName,
                        CellResult = true,
                        CompletedTime = completedTime,
                        CntActualKg = 12.5d
                    }
                }
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Equal("/api/v1/edge/process-records", cloudHttp.LastPostUrl);
        Assert.False(string.IsNullOrWhiteSpace(cloudHttp.LastPostOptions?.IdempotencyKey));
        Assert.IsType<HomogenizationProcessRecordsCloudPayload>(cloudHttp.LastPayload);

        var json = JsonSerializer.SerializeToElement(cloudHttp.LastPayload);
        Assert.Equal(HomogenizationModuleIdentity.ProcessType, json.GetProperty("typeKey").GetString());
        Assert.Equal(HomogenizationModuleIdentity.ProcessType, json.GetProperty("processType").GetString());
        Assert.Equal(1, json.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(device.DeviceId, json.GetProperty("deviceId").GetGuid());

        var records = json.GetProperty("records");
        Assert.Equal(JsonValueKind.Array, records.ValueKind);
        var record = records.EnumerateArray().Single();
        Assert.Equal(HomogenizationModuleIdentity.ProcessType, record.GetProperty("typeKey").GetString());
        Assert.Equal(HomogenizationModuleIdentity.ProcessType, record.GetProperty("processType").GetString());
        Assert.Equal(1, record.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(device.DeviceId, record.GetProperty("deviceId").GetGuid());
        Assert.Equal("TRAY-H-001", record.GetProperty("barcode").GetString());
        Assert.True(record.GetProperty("cellResult").GetBoolean());
        Assert.Equal(completedTime, record.GetProperty("completedTime").GetDateTime());

        var payload = record.GetProperty("payload");
        Assert.Equal("PLC-H", payload.GetProperty("plcName").GetString());
        Assert.Equal(12.5d, payload.GetProperty("cntActualKg").GetDouble());
        Assert.False(payload.TryGetProperty("plcDeviceId", out _));
    }
}
