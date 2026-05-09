using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.PassStation;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class CloudConsumerBehaviorTests
{
    [Fact]
    public async Task ProcessAsync_WhenCloudUploadDisabled_ShouldSkipUploaderAndReturnSuccess()
    {
        var uploader = new CapturingCloudUploader();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudUploadGate(UploadGateSnapshot.Blocked(
                ExternalSystemKind.Cloud,
                "cloud_upload_disabled",
                "云端上传已关闭。")),
            [uploader],
            new FakeCloudDiagnosticsStore(),
            new FakeLogService());

        var result = await consumer.ProcessAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-CLOUD-DISABLED"
            }
        });

        Assert.True(result);
        Assert.Equal(0, uploader.CallCount);
    }

    private static FakeDeviceService CreateOnlineDeviceService()
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-CLOUD",
            ClientCode = "CLIENT-CLOUD",
            ProcessId = Guid.NewGuid()
        });
        return deviceService;
    }

    private sealed class FixedCloudUploadGate(UploadGateSnapshot snapshot) : ICloudUploadGate
    {
        public ExternalSystemKind System => ExternalSystemKind.Cloud;

        public UploadGateSnapshot GetSnapshot() => snapshot;
    }

    private sealed class CapturingCloudUploader : IProcessCloudUploader
    {
        public int CallCount { get; private set; }

        public string ProcessType => "OtherProcess";

        public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

        public Task<CloudCallResult> UploadAsync(
            ProcessCloudUploadContext context,
            IReadOnlyList<CellCompletedRecord> records,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(CloudCallResult.Success());
        }
    }
}
