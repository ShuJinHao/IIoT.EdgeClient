using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.TestSimulator.Fakes;
using IIoT.Edge.TestSimulator.Services;
using IIoT.Edge.TestSimulator.Tasks;

namespace IIoT.Edge.TestSimulator.Scenarios;

/// <summary>
/// 重试场景
///
/// 前置：先切到 Online，并重置 HTTP 缓存。
/// 执行：触发一次重试任务，并等待任务提交完成。
/// 验证：确保 FailedRecordStore(Cloud)=0、CapacityBuffer=0，并验证 Cloud 批量补传按预期触发。
/// </summary>
public sealed class RetryScenario : ITestScenario
{
    public string Name => "离线重试补传场景";

    private readonly FakeHttpClient _httpClient;
    private readonly FakeDeviceService _deviceService;
    private readonly TestRetryTask _retryTask;
    private readonly SimDataHelper _dataHelper;
    private readonly IFailedRecordStore _failedStore;
    private readonly ICapacityBufferStore _bufferStore;
    private readonly ILogService _logger;

    public RetryScenario(
        FakeHttpClient httpClient,
        FakeDeviceService deviceService,
        TestRetryTask retryTask,
        SimDataHelper dataHelper,
        IFailedRecordStore failedStore,
        ICapacityBufferStore bufferStore,
        ILogService logger)
    {
        _httpClient = httpClient;
        _deviceService = deviceService;
        _retryTask = retryTask;
        _dataHelper = dataHelper;
        _failedStore = failedStore;
        _bufferStore = bufferStore;
        _logger = logger;
    }

    public async Task<ScenarioResult> RunAsync(CancellationToken ct = default)
    {
        var assertions = new List<AssertionResult>();

        try
        {
            // 前置：先切到 Online，确保有网络可用。
            _deviceService.CurrentState = NetworkState.Online;
            _httpClient.IsOnline = true;
            _httpClient.Reset();

            // 重置最近 30 秒的失败计数，避免历史状态影响当前重试验证。
            await _dataHelper.ResetRetryTimesAsync();

            // 执行：触发一次重试，并等待一次完整执行。
            await _retryTask.TriggerAsync();
            await Task.Delay(300, ct); // 等待异步任务完成一次迭代。

            var failedCount = await _failedStore.GetCountAsync("Cloud");
            var bufferCount = await _bufferStore.GetCountAsync();
            var callCount = _httpClient.CallCount;
            var batchCalls = _httpClient.UrlHistory.Count(u =>
                u.Contains("/api/test/passstation/batch", StringComparison.OrdinalIgnoreCase));
            var hasLegacyIdentityField = _httpClient.PayloadHistory.Any(p =>
                p.Contains("\"macAddress\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"mac_address\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"clientCode\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"client_code\"", StringComparison.OrdinalIgnoreCase));

            assertions.Add(Assert("FailedRecordStore(Cloud) == 0", failedCount == 0, "0", failedCount.ToString()));
            assertions.Add(Assert("CapacityBufferStore == 0", bufferCount == 0, "0", bufferCount.ToString()));
            assertions.Add(Assert("SimCloud batch call == 1", batchCalls == 1, "1", batchCalls.ToString()));
            assertions.Add(Assert("FakeHttpClient.CallCount >= 2", callCount >= 2, ">=2", callCount.ToString()));
            assertions.Add(Assert("Payload has no legacy identity field", !hasLegacyIdentityField, "false", hasLegacyIdentityField.ToString()));
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

    private static AssertionResult Assert(string desc, bool passed, string expected, string actual)
        => new() { Description = desc, Passed = passed, Expected = expected, Actual = actual };
}
