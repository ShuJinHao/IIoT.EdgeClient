using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.TestSimulator.Fakes;
using IIoT.Edge.TestSimulator.Services;
using IIoT.Edge.TestSimulator.Tasks;

namespace IIoT.Edge.TestSimulator.Scenarios;

/// <summary>
/// Cloud 重试场景。
/// 设备恢复 online 后，CloudRetryTask 应消费 cloud retry store 并完成补传。
/// </summary>
public sealed class RetryScenario : ITestScenario
{
    public string Name => "离线重试补传场景";

    private readonly FakeHttpClient _httpClient;
    private readonly FakeDeviceService _deviceService;
    private readonly TestRetryTask _retryTask;
    private readonly SimDataHelper _dataHelper;
    private readonly ICloudRetryRecordStore _cloudRetryStore;
    private readonly ICapacityBufferStore _bufferStore;
    private readonly ILogService _logger;

    public RetryScenario(
        FakeHttpClient httpClient,
        FakeDeviceService deviceService,
        TestRetryTask retryTask,
        SimDataHelper dataHelper,
        ICloudRetryRecordStore cloudRetryStore,
        ICapacityBufferStore bufferStore,
        ILogService logger)
    {
        _httpClient = httpClient;
        _deviceService = deviceService;
        _retryTask = retryTask;
        _dataHelper = dataHelper;
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

            await _dataHelper.ResetRetryTimesAsync();

            await _retryTask.TriggerAsync();
            await Task.Delay(300, ct);

            var failedCount = await _cloudRetryStore.GetCountAsync();
            var bufferCount = await _bufferStore.GetCountAsync();
            var callCount = _httpClient.CallCount;
            var batchCalls = _httpClient.UrlHistory.Count(u =>
                u.Contains("/api/test/passstation/batch", StringComparison.OrdinalIgnoreCase));
            var hasLegacyIdentityField = _httpClient.PayloadHistory.Any(p =>
                p.Contains("\"macAddress\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"mac_address\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"clientCode\"", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("\"client_code\"", StringComparison.OrdinalIgnoreCase));

            assertions.Add(Assert("CloudRetryStore == 0", failedCount == 0, "0", failedCount.ToString()));
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
