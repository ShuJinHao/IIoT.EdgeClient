using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Integration.Cloud;
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
    public async Task HomogenizationCloudUploader_WhenCloudContractUnconfirmed_ShouldSkipWithoutPosting()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var logger = new FakeLogService();
        var uploader = new HomogenizationCloudUploader(cloudHttp, logger);
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
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Null(cloudHttp.LastPostUrl);
        Assert.Null(cloudHttp.LastPayload);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == "Warn"
                     && entry.Message.Contains("匀浆云端上传未启用", StringComparison.Ordinal));
    }
}
