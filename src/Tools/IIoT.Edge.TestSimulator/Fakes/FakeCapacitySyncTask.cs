using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;

namespace IIoT.Edge.TestSimulator.Fakes;

/// <summary>
/// 简化版产能同步任务：不启动定时循环，RetryBufferAsync 直接汇总补传并清空缓冲
/// </summary>
public sealed class FakeCapacitySyncTask : ICapacitySyncTask
{
    private readonly ICloudHttpClient  _httpClient;
    private readonly ICapacityBufferStore _bufferStore;
    private readonly ILogService       _logger;

    public FakeCapacitySyncTask(
        ICloudHttpClient cloudHttp,
        ICapacityBufferStore bufferStore,
        ILogService logger)
    {
        _httpClient  = cloudHttp;
        _bufferStore = bufferStore;
        _logger      = logger;
    }

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync()                       => Task.CompletedTask;

    public async Task<bool> RetryBufferAsync()
    {
        var summaries = await _bufferStore.GetShiftSummaryAsync();
        if (summaries.Count == 0)
        {
            _logger.Info("[FakeCapacitySync] 产能缓冲为空，无需补传");
            return true;
        }

        foreach (var s in summaries)
            await _httpClient.PostAsync("/api/test/capacity/retry", s);

        await _bufferStore.ClearAllAsync();
        _logger.Info($"[FakeCapacitySync] 补传完成，共 {summaries.Count} 条班次汇总，缓冲已清空");
        return true;
    }
}
