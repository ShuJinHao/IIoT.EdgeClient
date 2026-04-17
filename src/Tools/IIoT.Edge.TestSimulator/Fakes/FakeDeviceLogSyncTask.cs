using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;

namespace IIoT.Edge.TestSimulator.Fakes;

/// <summary>
/// 空实现：测试中不需要日志同步，RetryBufferAsync 直接返回 true
/// </summary>
public sealed class FakeDeviceLogSyncTask : IDeviceLogSyncTask
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync()                       => Task.CompletedTask;
    public Task<bool> RetryBufferAsync()          => Task.FromResult(true);
}
