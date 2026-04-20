using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.TestSimulator.Fakes;
using IIoT.Edge.TestSimulator.Scenarios;

namespace IIoT.Edge.TestSimulator.Services;

/// <summary>
/// 场景执行器。
/// 支持顺序执行全部场景或按名称选择执行。
/// </summary>
public sealed class ScenarioRunner
{
    private readonly OnlinePassScenario _scenario1;
    private readonly OfflineBufferScenario _scenario2;
    private readonly RetryScenario _scenario3;
    private readonly HistoricalDataScenario _scenario4;
    private readonly SimDataHelper _dataHelper;
    private readonly FakeHttpClient _httpClient;
    private readonly FakeDeviceService _deviceService;
    private readonly FakeTodayCapacityStore _capacityStore;
    private readonly ILogService _logger;
    private readonly IReadOnlyList<ITestScenario> _allScenarios;

    public ScenarioRunner(
        OnlinePassScenario scenario1,
        OfflineBufferScenario scenario2,
        RetryScenario scenario3,
        HistoricalDataScenario scenario4,
        SimDataHelper dataHelper,
        FakeHttpClient httpClient,
        FakeDeviceService deviceService,
        FakeTodayCapacityStore capacityStore,
        ILogService logger)
    {
        _scenario1 = scenario1;
        _scenario2 = scenario2;
        _scenario3 = scenario3;
        _scenario4 = scenario4;
        _dataHelper = dataHelper;
        _httpClient = httpClient;
        _deviceService = deviceService;
        _capacityStore = capacityStore;
        _logger = logger;
        _allScenarios = [_scenario1, _scenario2, _scenario3, _scenario4];
    }

    public Task<List<ScenarioResult>> RunAllAsync(CancellationToken ct = default)
        => RunInternalAsync(_allScenarios, resetBeforeRun: true, ct);

    public Task<List<ScenarioResult>> RunSelectedAsync(
        IReadOnlyCollection<string> selectedScenarioNames,
        bool resetBeforeRun,
        CancellationToken ct = default)
    {
        var selected = _allScenarios
            .Where(s => selectedScenarioNames.Contains(s.Name))
            .ToList();

        return RunInternalAsync(selected, resetBeforeRun, ct);
    }

    public IReadOnlyList<string> GetAllScenarioNames()
        => _allScenarios.Select(s => s.Name).ToList();

    private async Task<List<ScenarioResult>> RunInternalAsync(
        IReadOnlyList<ITestScenario> scenarios,
        bool resetBeforeRun,
        CancellationToken ct)
    {
        var results = new List<ScenarioResult>();

        if (scenarios.Count == 0)
        {
            _logger.Warn("[ScenarioRunner] 未选择任何场景，已跳过执行");
            return results;
        }

        _logger.Info("----------------------------------------");
        _logger.Info("    IIoT Edge 集成测试模拟器 开始运行");
        _logger.Info("----------------------------------------");

        var needReset = resetBeforeRun && scenarios.Any(s => s is not HistoricalDataScenario);
        if (needReset)
        {
            await _dataHelper.ClearAllAsync();
            _capacityStore.ResetAll();
            _logger.Info("[ScenarioRunner] 运行前已清空 SQLite 与内存状态");
        }

        for (var i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            _logger.Info(string.Empty);
            _logger.Info($"场景 {i + 1} / {scenarios.Count}: {scenario.Name}");

            ScenarioResult result;
            try
            {
                result = await scenario.RunAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.Error($"[ScenarioRunner] {scenario.Name} 未捕获异常: {ex.Message}");
                result = new ScenarioResult
                {
                    Name = scenario.Name,
                    Passed = false,
                    Error = ex.Message
                };
            }

            results.Add(result);

            foreach (var assertion in result.Assertions)
            {
                _logger.Info($"  {assertion}");
            }

            if (result.Error is not null)
            {
                _logger.Error($"  异常: {result.Error}");
            }

            _logger.Info($"  {(result.Passed ? "通过" : "失败")}");
        }

        var passCount = results.Count(r => r.Passed);
        _logger.Info(string.Empty);
        _logger.Info("----------------------------------------");
        _logger.Info($"    总结: {passCount} / {results.Count} 通过");
        _logger.Info("----------------------------------------");

        return results;
    }

    public async Task ResetAsync()
    {
        await _dataHelper.ClearAllAsync();
        _httpClient.Reset();
        _capacityStore.ResetAll();
        _deviceService.SetOffline();
        _logger.Info("[重置] 所有测试数据已清空，状态已恢复初始值");
    }
}
