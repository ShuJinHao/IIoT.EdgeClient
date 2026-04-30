using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationCloudUploaderBehaviorTests
{
    [Fact]
    public async Task HomogenizationCloudUploader_WhenCloudContractIsLocked_ShouldSkipWithoutRetryFailure()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var logger = new FakeLogService();
        var uploader = new HomogenizationCloudUploader(cloudHttp, logger);
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
                        CompletedTime = new DateTime(2026, 4, 30, 8, 0, 0, DateTimeKind.Utc)
                    }
                }
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Contains(
            logger.Entries,
            entry => entry.Message.Contains("匀浆云端上传未启用", StringComparison.Ordinal));
    }
}
