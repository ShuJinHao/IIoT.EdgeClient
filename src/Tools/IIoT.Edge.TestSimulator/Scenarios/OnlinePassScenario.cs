using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.TestSimulator.Fakes;
using IIoT.Edge.Runtime.DataPipeline.Services;

namespace IIoT.Edge.TestSimulator.Scenarios;

/// <summary>
/// 场景一：在线正常上报
/// 
/// 前置：Online + HTTP 在线
/// 执行：入队 3 条 TEST-001~003
/// 断言：HTTP 调用 3 次 / FailedStore=0 / CapacityBuffer=0 / TodayCapacity=3
/// </summary>
public sealed class OnlinePassScenario : ITestScenario
{
    public string Name => "场景一：在线正常上报";

    private readonly FakeHttpClient       _httpClient;
    private readonly FakeDeviceService    _deviceService;
    private readonly FakeTodayCapacityStore _capacityStore;
    private readonly DataPipelineService  _pipeline;
    private readonly IFailedRecordStore   _failedStore;
    private readonly ICapacityBufferStore _bufferStore;
    private readonly ILogService          _logger;

    public OnlinePassScenario(
        FakeHttpClient          httpClient,
        FakeDeviceService       deviceService,
        FakeTodayCapacityStore  capacityStore,
        DataPipelineService     pipeline,
        IFailedRecordStore      failedStore,
        ICapacityBufferStore    bufferStore,
        ILogService             logger)
    {
        _httpClient    = httpClient;
        _deviceService = deviceService;
        _capacityStore = capacityStore;
        _pipeline      = pipeline;
        _failedStore   = failedStore;
        _bufferStore   = bufferStore;
        _logger        = logger;
    }

    public async Task<ScenarioResult> RunAsync(CancellationToken ct = default)
    {
        var assertions = new List<AssertionResult>();

        try
        {
            // 前置
            _deviceService.CurrentState = NetworkState.Online;
            _httpClient.IsOnline        = true;
            _httpClient.Reset();
            _capacityStore.ResetAll();

            // 执行
            for (int i = 1; i <= 3; i++)
                _pipeline.Enqueue(BuildRecord($"TEST-{i:D3}"));

            await WaitQueueEmptyAsync(ct);
            await Task.Delay(200, ct); // 等待异步 Store 写入完成

            // 断言
            var callCount     = _httpClient.CallCount;
            var failedCount   = await _failedStore.GetCountAsync("Cloud");
            var bufferCount   = await _bufferStore.GetCountAsync();
            var capacityTotal = _capacityStore.GetSnapshot("TestDevice").TotalAll;
            var hasLegacyIdentityField = _httpClient.PayloadHistory.Any(p =>
                p.Contains("\"macAddress\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"mac_address\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"clientCode\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"client_code\"", StringComparison.OrdinalIgnoreCase));

            assertions.Add(Assert("FakeHttpClient.CallCount == 3",    callCount     == 3, "3", callCount.ToString()));
            assertions.Add(Assert("FailedRecordStore(Cloud) == 0",    failedCount   == 0, "0", failedCount.ToString()));
            assertions.Add(Assert("CapacityBufferStore == 0",         bufferCount   == 0, "0", bufferCount.ToString()));
            assertions.Add(Assert("TodayCapacity.TotalAll == 3",      capacityTotal == 3, "3", capacityTotal.ToString()));
            assertions.Add(Assert("Payload has no mac/clientCode",   !hasLegacyIdentityField, "false", hasLegacyIdentityField.ToString()));
        }
        catch (Exception ex)
        {
            _logger.Error($"[{Name}] 异常: {ex.Message}");
            return new ScenarioResult { Name = Name, Passed = false, Error = ex.Message, Assertions = assertions };
        }

        return new ScenarioResult
        {
            Name       = Name,
            Passed     = assertions.All(a => a.Passed),
            Assertions = assertions
        };
    }

    // 辅助方法

    private async Task WaitQueueEmptyAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_pipeline.PendingCount > 0 && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            await Task.Delay(80, ct);
    }

    private static CellCompletedRecord BuildRecord(string barcode) => new()
    {
        CellData = new InjectionCellData
        {
            DeviceName           = "TestDevice",
            DeviceCode           = "SIM-001",
            Barcode              = barcode,
            CellResult           = true,
            CompletedTime        = DateTime.Now,
            ScanTime             = DateTime.Now,
            PreInjectionWeight   = 25.0,
            PostInjectionWeight  = 28.0,
            InjectionVolume      = 3.0
        }
    };

    private static AssertionResult Assert(string desc, bool passed, string expected, string actual)
        => new() { Description = desc, Passed = passed, Expected = expected, Actual = actual };
}

