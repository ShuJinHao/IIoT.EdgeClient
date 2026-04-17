using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.TestSimulator.Fakes;
using IIoT.Edge.Runtime.DataPipeline.Services;

namespace IIoT.Edge.TestSimulator.Scenarios;

/// <summary>
/// 场景二：离线落库
/// 
/// 前置：Offline + HTTP 断线 + 清空上一场景 HTTP 历史
/// 执行：入队 5 条 OFFLINE-001~005
/// 断言：FailedStore(Cloud)=5 / CapacityBuffer=5 / HTTP 调用=0
/// </summary>
public sealed class OfflineBufferScenario : ITestScenario
{
    public string Name => "场景二：离线落库";

    private readonly FakeHttpClient      _httpClient;
    private readonly FakeDeviceService   _deviceService;
    private readonly DataPipelineService _pipeline;
    private readonly IFailedRecordStore  _failedStore;
    private readonly ICapacityBufferStore _bufferStore;
    private readonly ILogService         _logger;

    public OfflineBufferScenario(
        FakeHttpClient       httpClient,
        FakeDeviceService    deviceService,
        DataPipelineService  pipeline,
        IFailedRecordStore   failedStore,
        ICapacityBufferStore bufferStore,
        ILogService          logger)
    {
        _httpClient    = httpClient;
        _deviceService = deviceService;
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
            _deviceService.CurrentState = NetworkState.Offline;
            _httpClient.IsOnline        = false;
            _httpClient.Reset();

            // 执行
            for (int i = 1; i <= 5; i++)
                _pipeline.Enqueue(BuildRecord($"OFFLINE-{i:D3}"));

            await WaitQueueEmptyAsync(ct);
            await Task.Delay(300, ct); // 等待所有 Store 写入落盘

            // 断言
            var failedCount = await _failedStore.GetCountAsync("Cloud");
            var bufferCount = await _bufferStore.GetCountAsync();
            var callCount   = _httpClient.CallCount;

            assertions.Add(Assert("FailedRecordStore(Cloud) == 5", failedCount == 5, "5", failedCount.ToString()));
            assertions.Add(Assert("CapacityBufferStore == 5",       bufferCount == 5, "5", bufferCount.ToString()));
            assertions.Add(Assert("FakeHttpClient.CallCount == 0",  callCount   == 0, "0", callCount.ToString()));
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
            DeviceName          = "TestDevice",
            DeviceCode          = "SIM-001",
            Barcode             = barcode,
            CellResult          = false,
            CompletedTime       = DateTime.Now,
            ScanTime            = DateTime.Now,
            PreInjectionWeight  = 25.0,
            PostInjectionWeight = 27.5,
            InjectionVolume     = 2.5
        }
    };

    private static AssertionResult Assert(string desc, bool passed, string expected, string actual)
        => new() { Description = desc, Passed = passed, Expected = expected, Actual = actual };
}

