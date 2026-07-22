using System.Text.Json;
using System.Text.Json.Serialization;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Infrastructure.Integration.PassStation;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

namespace IIoT.Edge.Cloud.ContractTests;

public sealed class StandardPassStationCloudUploaderContractTests
{
    [Fact]
    public async Task UploadAsync_AtMaximumBatchAndTextBoundaries_ShouldSendCanonicalEnvelopeOnce()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var uploader = CreateUploader(cloudHttp);
        var record = CreateRecord(new string('B', PassStationCloudContract.MaxBarcodeLength));
        var records = Enumerable.Repeat(record, PassStationCloudContract.MaxItems).ToArray();

        var result = await uploader.UploadAsync(
            CreateContext(),
            new string('P', PassStationCloudContract.MaxProcessTypeLength),
            records,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, cloudHttp.PostCallCount);
        var payload = Assert.IsType<PassStationBatchUploadPayload>(cloudHttp.LastPayload);
        Assert.Equal(PassStationCloudContract.MaxItems, payload.Items.Count);
        Assert.Equal(new string('p', PassStationCloudContract.MaxProcessTypeLength), payload.ProcessType);
        Assert.Equal(PassStationCloudContract.SchemaVersion, payload.SchemaVersion);
        Assert.NotNull(payload.RequestId);
        Assert.Equal(64, payload.RequestId.Length);
        Assert.Equal(payload.RequestId, cloudHttp.LastPostOptions?.IdempotencyKey);
    }

    [Theory]
    [InlineData(0, "pass_station_items_required")]
    [InlineData(PassStationCloudContract.MaxItems + 1, "pass_station_items_limit_exceeded")]
    public async Task UploadAsync_WhenItemCountViolatesProviderRange_ShouldRejectBeforeHttp(
        int count,
        string expectedReason)
    {
        var cloudHttp = new FakeCloudHttpClient();
        var uploader = CreateUploader(cloudHttp);
        var records = Enumerable.Repeat(CreateRecord("BC-COUNT"), count).ToArray();

        var result = await uploader.UploadAsync(
            CreateContext(),
            "TestProcess",
            records,
            TestContext.Current.CancellationToken);

        Assert.Equal(CloudCallOutcome.InvalidPayload, result.Outcome);
        Assert.Equal(expectedReason, result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
    }

    [Fact]
    public async Task UploadAsync_WhenDeviceIdIsEmpty_ShouldRejectBeforeHttp()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var result = await CreateUploader(cloudHttp).UploadAsync(
            CreateContext(Guid.Empty),
            "TestProcess",
            [CreateRecord("BC-DEVICE")],
            TestContext.Current.CancellationToken);

        Assert.Equal(CloudCallOutcome.InvalidPayload, result.Outcome);
        Assert.Equal("pass_station_device_id_required", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
    }

    [Theory]
    [InlineData("", "pass_station_process_type_required")]
    [InlineData("                                 ", "pass_station_process_type_required")]
    [InlineData("123456789012345678901234567890123", "pass_station_process_type_too_long")]
    public async Task UploadAsync_WhenProcessTypeViolatesProviderContract_ShouldRejectBeforeHttp(
        string processType,
        string expectedReason)
    {
        var cloudHttp = new FakeCloudHttpClient();
        var result = await CreateUploader(cloudHttp).UploadAsync(
            CreateContext(),
            processType,
            [CreateRecord("BC-PROCESS")],
            TestContext.Current.CancellationToken);

        Assert.Equal(CloudCallOutcome.InvalidPayload, result.Outcome);
        Assert.Equal(expectedReason, result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
    }

    [Fact]
    public async Task UploadAsync_WhenBarcodeExceedsProviderLimit_ShouldRejectWholeBatchBeforeHttp()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var result = await CreateUploader(cloudHttp).UploadAsync(
            CreateContext(),
            "TestProcess",
            [
                CreateRecord("BC-VALID"),
                CreateRecord(new string('B', PassStationCloudContract.MaxBarcodeLength + 1))
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(CloudCallOutcome.InvalidPayload, result.Outcome);
        Assert.Equal("pass_station_barcode_too_long", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
    }

    [Fact]
    public async Task UploadAsync_WhenSerializedPayloadExceedsProviderFieldLimit_ShouldRejectBeforeHttp()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var cellData = new ExtensionPayloadCellData
        {
            Barcode = "BC-PAYLOAD",
            CellResult = true,
            CompletedTime = DateTime.UtcNow,
            ExtensionFields = Enumerable.Range(0, PassStationCloudContract.MaxPayloadProperties + 1)
                .ToDictionary(
                    index => $"extra{index}",
                    index => JsonSerializer.SerializeToElement(index),
                    StringComparer.Ordinal)
        };

        var result = await CreateUploader(cloudHttp).UploadAsync(
            CreateContext(),
            "TestProcess",
            [new CellCompletedRecord { CellData = cellData }],
            TestContext.Current.CancellationToken);

        Assert.Equal(CloudCallOutcome.InvalidPayload, result.Outcome);
        Assert.Equal("pass_station_payload_properties_limit_exceeded", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
    }

    private static StandardPassStationCloudUploader CreateUploader(FakeCloudHttpClient cloudHttp) =>
        new(new FakeCloudApiEndpointProvider(), cloudHttp);

    private static ProcessUploadContext CreateContext(Guid? deviceId = null) => new(new DeviceSession
    {
        DeviceId = deviceId ?? Guid.Parse("22222222-2222-2222-2222-222222222222"),
        DeviceName = "TEST-PLC",
        ClientCode = "TEST-CLIENT",
        ProcessId = Guid.Parse("33333333-3333-3333-3333-333333333333")
    });

    private static CellCompletedRecord CreateRecord(string barcode) => new()
    {
        CellData = new TestCellData
        {
            Barcode = barcode,
            CellResult = true,
            CompletedTime = DateTime.UtcNow
        }
    };

    private sealed class ExtensionPayloadCellData : CellDataBase
    {
        public override string ProcessType => "TestProcess";

        public override string DisplayLabel => Barcode;

        public string Barcode { get; init; } = string.Empty;

        [JsonExtensionData]
        public IDictionary<string, JsonElement> ExtensionFields { get; init; } =
            new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    }
}
