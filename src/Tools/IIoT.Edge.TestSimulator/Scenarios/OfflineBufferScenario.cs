using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.TestSimulator.Fakes;

namespace IIoT.Edge.TestSimulator.Scenarios;

/// <summary>
/// 离线落库场景。
/// Cloud 门禁 blocked 时，实时数据应进入 Cloud 重传库；产能数据继续写本地 buffer。
/// </summary>
public sealed class OfflineBufferScenario : ITestScenario
{
    public string Name => "场景二：离线落库";

    private readonly FakeHttpClient _httpClient;
    private readonly FakeDeviceService _deviceService;
    private readonly DataPipelineService _pipeline;
    private readonly ICloudRetryRecordStore _cloudRetryStore;
    private readonly ICapacityBufferStore _bufferStore;
    private readonly ILogService _logger;

    public OfflineBufferScenario(
        FakeHttpClient httpClient,
        FakeDeviceService deviceService,
        DataPipelineService pipeline,
        ICloudRetryRecordStore cloudRetryStore,
        ICapacityBufferStore bufferStore,
        ILogService logger)
    {
        _httpClient = httpClient;
        _deviceService = deviceService;
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
            _deviceService.SetOffline();
            _httpClient.IsOnline = false;
            _httpClient.Reset();

            for (var i = 1; i <= 5; i++)
            {
                await _pipeline.EnqueueAsync(BuildRecord($"OFFLINE-{i:D3}"), ct);
            }

            await WaitQueueEmptyAsync(ct);
            await Task.Delay(300, ct);

            var failedCount = await _cloudRetryStore.GetCountAsync();
            var bufferCount = await _bufferStore.GetCountAsync();
            var callCount = _httpClient.CallCount;

            assertions.Add(Assert("CloudRetryStore == 5", failedCount == 5, "5", failedCount.ToString()));
            assertions.Add(Assert("CapacityBufferStore == 5", bufferCount == 5, "5", bufferCount.ToString()));
            assertions.Add(Assert("FakeHttpClient.CallCount == 0", callCount == 0, "0", callCount.ToString()));
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
            CellResult = false,
            CompletedTime = DateTime.Now,
            ScanTime = DateTime.Now,
            PreInjectionWeight = 25.0,
            PostInjectionWeight = 27.5,
            InjectionVolume = 2.5
        }
    };

    private static AssertionResult Assert(string desc, bool passed, string expected, string actual)
        => new() { Description = desc, Passed = passed, Expected = expected, Actual = actual };
}
