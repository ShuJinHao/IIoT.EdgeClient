using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.TestSimulator.Fakes;

namespace IIoT.Edge.TestSimulator.Scenarios;

/// <summary>
/// 在线直通场景。
/// Cloud upload gate 就绪时，应直接上传，不写 Cloud 重传库。
/// </summary>
public sealed class OnlinePassScenario : ITestScenario
{
    public string Name => "场景一：在线正常上报";

    private readonly FakeHttpClient _httpClient;
    private readonly FakeDeviceService _deviceService;
    private readonly FakeTodayCapacityStore _capacityStore;
    private readonly DataPipelineService _pipeline;
    private readonly ICloudRetryRecordStore _cloudRetryStore;
    private readonly ICapacityBufferStore _bufferStore;
    private readonly ILogService _logger;

    public OnlinePassScenario(
        FakeHttpClient httpClient,
        FakeDeviceService deviceService,
        FakeTodayCapacityStore capacityStore,
        DataPipelineService pipeline,
        ICloudRetryRecordStore cloudRetryStore,
        ICapacityBufferStore bufferStore,
        ILogService logger)
    {
        _httpClient = httpClient;
        _deviceService = deviceService;
        _capacityStore = capacityStore;
        _pipeline = pipeline;
        _cloudRetryStore = cloudRetryStore;
        _bufferStore = bufferStore;
        _logger = logger;
    }

    public async Task<ScenarioResult> RunAsync(CancellationToken ct = default)
    {
        var assertions = new List<AssertionResult>();

        try
        {
            _deviceService.SetOnline();
            _httpClient.IsOnline = true;
            _httpClient.Reset();
            _capacityStore.ResetAll();

            for (var i = 1; i <= 3; i++)
            {
                await _pipeline.EnqueueAsync(BuildRecord($"TEST-{i:D3}"), ct);
            }

            await WaitQueueEmptyAsync(ct);
            await Task.Delay(200, ct);

            var callCount = _httpClient.CallCount;
            var failedCount = await _cloudRetryStore.GetCountAsync();
            var bufferCount = await _bufferStore.GetCountAsync();
            var capacityTotal = _capacityStore.GetSnapshot("TestDevice").TotalAll;
            var hasLegacyIdentityField = _httpClient.PayloadHistory.Any(p =>
                p.Contains("\"macAddress\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"mac_address\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"clientCode\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"client_code\"", StringComparison.OrdinalIgnoreCase));

            assertions.Add(Assert("FakeHttpClient.CallCount == 3", callCount == 3, "3", callCount.ToString()));
            assertions.Add(Assert("CloudRetryStore == 0", failedCount == 0, "0", failedCount.ToString()));
            assertions.Add(Assert("CapacityBufferStore == 0", bufferCount == 0, "0", bufferCount.ToString()));
            assertions.Add(Assert("TodayCapacity.TotalAll == 3", capacityTotal == 3, "3", capacityTotal.ToString()));
            assertions.Add(Assert("Payload has no mac/clientCode", !hasLegacyIdentityField, "false", hasLegacyIdentityField.ToString()));
        }
        catch (Exception ex)
        {
            _logger.Error($"[{Name}] 异常: {ex.Message}");
            return new ScenarioResult { Name = Name, Passed = false, Error = ex.Message, Assertions = assertions };
        }

        return new ScenarioResult
        {
            Name = Name,
            Passed = assertions.All(a => a.Passed),
            Assertions = assertions
        };
    }

    private async Task WaitQueueEmptyAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_pipeline.PendingCount > 0 && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(80, ct);
        }
    }

    private static CellCompletedRecord BuildRecord(string barcode) => new()
    {
        CellData = new InjectionCellData
        {
            DeviceName = "TestDevice",
            DeviceCode = "SIM-001",
            Barcode = barcode,
            CellResult = true,
            CompletedTime = DateTime.Now,
            ScanTime = DateTime.Now,
            PreInjectionWeight = 25.0,
            PostInjectionWeight = 28.0,
            InjectionVolume = 3.0
        }
    };

    private static AssertionResult Assert(string desc, bool passed, string expected, string actual)
        => new() { Description = desc, Passed = passed, Expected = expected, Actual = actual };
}
