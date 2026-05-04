using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Injection.Integration;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class InjectionCloudUploaderBehaviorTests
{
    [Fact]
    public async Task InjectionCloudUploader_WhenCloudContractIsNotReady_ShouldSkipWithoutPosting()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var logger = new FakeLogService();
        var uploader = new InjectionCloudUploader(
            cloudHttp,
            new FakeProductionTimeProvider(),
            logger);
        var device = new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-I",
            ClientCode = "CLIENT-I",
            ProcessId = Guid.NewGuid()
        };

        var result = await uploader.UploadAsync(
            new ProcessCloudUploadContext(device),
            [
                new CellCompletedRecord
                {
                    CellData = new InjectionCellData
                    {
                        Barcode = "INJ-001",
                        DeviceName = device.DeviceName,
                        CompletedTime = new DateTime(2026, 5, 3, 8, 0, 0, DateTimeKind.Utc)
                    }
                }
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Contains(
            logger.Entries,
            entry => entry.Message.Contains("注液云端上传契约未就绪", StringComparison.Ordinal));
    }
}
