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

    [Fact]
    public async Task UploadAsync_WhenApCpRecordsHaveMixedCompleteness_ShouldSplitStrictV2FromLegacyV1()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var completedTime = new DateTime(2026, 8, 2, 1, 30, 0, DateTimeKind.Utc);
        var strict = CreateCpRecord(
            "CP-STRICT-001",
            completedTime,
            plcName: "正极模切一号 PLC");
        var legacy = CreateCpRecord(
            "CP-LEGACY-001",
            completedTime.AddMinutes(1),
            plcName: string.Empty);

        var result = await CreateUploader(cloudHttp).UploadAsync(
            CreateContext(),
            " CP ",
            [strict, legacy],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, cloudHttp.PostCallCount);
        var strictPayload = Assert.IsType<PassStationBatchUploadPayload>(cloudHttp.PostPayloads[0]);
        var legacyPayload = Assert.IsType<PassStationBatchUploadPayload>(cloudHttp.PostPayloads[1]);
        Assert.Equal(PassStationCloudContract.LegacyStrictSchemaVersion, strictPayload.SchemaVersion);
        Assert.Equal("cp", strictPayload.ProcessType);
        Assert.Equal("CP-STRICT-001", Assert.Single(strictPayload.Items).Barcode);
        Assert.Equal(PassStationCloudContract.LegacySchemaVersion, legacyPayload.SchemaVersion);
        Assert.Equal("cp", legacyPayload.ProcessType);
        Assert.Equal("CP-LEGACY-001", Assert.Single(legacyPayload.Items).Barcode);
        Assert.NotEqual(strictPayload.RequestId, legacyPayload.RequestId);
        Assert.Equal(strictPayload.RequestId, cloudHttp.PostIdempotencyKeys[0]);
        Assert.Equal(legacyPayload.RequestId, cloudHttp.PostIdempotencyKeys[1]);
    }

    [Theory]
    [InlineData("MG3")]
    [InlineData("")]
    public async Task UploadAsync_WhenCpRecordViolatesStrictClipSlot_ShouldRemainCompatibleV1(
        string clipSlot)
    {
        var cloudHttp = new FakeCloudHttpClient();
        var record = CreateCpRecord(
            "CP-HISTORY-001",
            new DateTime(2026, 8, 2, 1, 30, 0, DateTimeKind.Utc),
            clipSlot: clipSlot);

        var result = await CreateUploader(cloudHttp).UploadAsync(
            CreateContext(),
            "cp",
            [record],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<PassStationBatchUploadPayload>(cloudHttp.LastPayload);
        Assert.Equal(PassStationCloudContract.LegacySchemaVersion, payload.SchemaVersion);
    }

    [Fact]
    public async Task UploadAsync_V3Item_ShouldCarryStableCompletionAndDeviceIdentity()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var record = CreateRecord("BC-V3");
        record.CompletionId = "completion-001";
        record.ClientCode = "test-client";
        record.ProcessType = "DieCut";
        record.TypeKey = "diecut.completed";
        record.PlcCode = "PLC-01";

        var result = await CreateUploader(cloudHttp).UploadAsync(
            CreateContext(),
            "diecut",
            [record],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<PassStationBatchUploadPayload>(cloudHttp.LastPayload);
        Assert.Equal(PassStationCloudContract.StrictSchemaVersion, payload.SchemaVersion);
        Assert.Equal("TEST-CLIENT", payload.ClientCode);
        Assert.Equal("diecut.completed", payload.TypeKey);
        var item = Assert.Single(payload.Items);
        Assert.Equal("completion-001", item.CompletionId);
        Assert.Equal("TEST-CLIENT", item.ClientCode);
        Assert.Equal("diecut.completed", item.TypeKey);
        Assert.Equal("PLC-01", item.PlcCode);
    }

    private static StandardPassStationCloudUploader CreateUploader(FakeCloudHttpClient cloudHttp) =>
        new(new FakeCloudApiEndpointProvider(), cloudHttp);

#pragma warning disable CS0618 // Cloud compatibility contract still consumes the v2 Host context.
    private static ProcessUploadContext CreateContext(Guid? deviceId = null) => new(new DeviceSession
    {
        DeviceId = deviceId ?? Guid.Parse("22222222-2222-2222-2222-222222222222"),
        DeviceName = "TEST-PLC",
        ClientCode = "TEST-CLIENT",
        ProcessId = Guid.Parse("33333333-3333-3333-3333-333333333333")
    });
#pragma warning restore CS0618

    private static CellCompletedRecord CreateRecord(string barcode) => new()
    {
        CellData = new TestCellData
        {
            Barcode = barcode,
            CellResult = true,
            CompletedTime = DateTime.UtcNow
        }
    };

    private static CellCompletedRecord CreateCpRecord(
        string barcode,
        DateTime completedTime,
        string plcName = "正极模切一号 PLC",
        string clipSlot = "MG1") => new()
    {
        CellData = new StrictCpCellData
        {
            Barcode = barcode,
            CellResult = true,
            CompletedTime = completedTime,
            PlcCode = "P2-CP01",
            PlcName = plcName,
            ClipSlot = clipSlot,
            StartTime = completedTime.AddMinutes(-5),
            PunchingQuantity = 120,
            PunchingSpeed = 1.25m
        }
    };

    private sealed class StrictCpCellData : CellDataBase
    {
        public override string ProcessType => "cp";

        public override string DisplayLabel => Barcode;

        public string Barcode { get; init; } = string.Empty;

        public string PlcCode { get; init; } = string.Empty;

        public string PlcName { get; init; } = string.Empty;

        public string ClipSlot { get; init; } = string.Empty;

        public DateTime StartTime { get; init; }

        public int PunchingQuantity { get; init; }

        public decimal PunchingSpeed { get; init; }
    }

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
