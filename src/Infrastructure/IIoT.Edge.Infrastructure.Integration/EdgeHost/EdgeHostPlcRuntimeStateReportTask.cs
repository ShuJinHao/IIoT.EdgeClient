using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Tasks;

namespace IIoT.Edge.Infrastructure.Integration.EdgeHost;

public sealed class EdgeHostPlcRuntimeStateReportTask(
    IEdgeHostPlcRuntimeStateReporter reporter,
    ILocalSystemRuntimeConfigService runtimeConfig,
    ILogService logger) : IStartupAwareBackgroundTask
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _reportGate = new(1, 1);

    public string TaskName => "Cloud.PlcRuntimeState";

    public Task StartAsync(CancellationToken ct)
        => StartWithStartup(ct).Execution;

    public BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken)
    {
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return new BackgroundTaskRun(startup.Task, RunAsync(cancellationToken, startup));
    }

    private async Task RunAsync(CancellationToken ct, TaskCompletionSource startup)
    {
        logger.Info($"[PLC 状态上报] 已启动，间隔：{ResolveInterval().TotalSeconds:0}s");
        startup.TrySetResult();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ResolveInterval(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await ExecuteOnceAsync(ct).ConfigureAwait(false);
        }

        logger.Info("[PLC 状态上报] 已停止。");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    internal async Task<EdgeHostPlcRuntimeStateReportResult?> ExecuteOnceAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _reportGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return EdgeHostPlcRuntimeStateReportResult.Skipped("report_in_flight");
        }

        try
        {
            var result = await reporter.ReportOnceAsync(cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                logger.Debug($"[PLC 状态上报] 已上报 {result.ReportedCount} 台 PLC 状态。");
            }
            else if (!string.IsNullOrWhiteSpace(result.ReasonCode))
            {
                logger.Debug($"[PLC 状态上报] 本轮跳过或失败：{result.ReasonCode}");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Debug($"[PLC 状态上报] 本轮执行异常，已跳过：{ex.Message}");
            return EdgeHostPlcRuntimeStateReportResult.Failed("exception");
        }
        finally
        {
            _reportGate.Release();
        }
    }

    private TimeSpan ResolveInterval()
    {
        var interval = runtimeConfig.Current.CloudSyncInterval;
        return interval < MinimumInterval || interval > MaximumInterval ? DefaultInterval : interval;
    }
}
