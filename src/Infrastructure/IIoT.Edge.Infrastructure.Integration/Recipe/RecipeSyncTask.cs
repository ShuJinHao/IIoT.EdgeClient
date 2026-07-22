using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Recipe;
using IIoT.Edge.Module.Contracts.Tasks;

namespace IIoT.Edge.Infrastructure.Integration.Recipe;

public sealed class RecipeSyncTask : IStartupAwareBackgroundTask
{
    private readonly IRecipeService _recipeService;
    private readonly IDeviceService _deviceService;
    private readonly ILogService _logger;
    private readonly TimeSpan _syncInterval;

    public RecipeSyncTask(
        IRecipeService recipeService,
        IDeviceService deviceService,
        ILogService logger,
        TimeSpan? syncInterval = null)
    {
        _recipeService = recipeService;
        _deviceService = deviceService;
        _logger = logger;
        _syncInterval = syncInterval ?? TimeSpan.FromSeconds(60);
    }

    public string TaskName => "Cloud.RecipeSync";

    public Task StartAsync(CancellationToken ct)
        => StartWithStartup(ct).Execution;

    public BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken)
    {
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return new BackgroundTaskRun(startup.Task, RunAsync(cancellationToken, startup));
    }

    private async Task RunAsync(CancellationToken ct, TaskCompletionSource startup)
    {
        _logger.Info($"[配方同步] 已启动，间隔：{_syncInterval.TotalSeconds:0}s");
        startup.TrySetResult();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_syncInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ExecuteOnceAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.Info("[配方同步] 已停止。");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    internal async Task ExecuteOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_deviceService.CanUploadToCloud || _deviceService.CurrentDevice is null)
        {
            return;
        }

        try
        {
            var synced = await _recipeService
                .PullFromCloudAsync(cancellationToken)
                .ConfigureAwait(false);
            if (synced)
            {
                _logger.Info("[配方同步] 云端配方缓存已刷新。");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[配方同步] 云端配方同步失败：{ex.Message}");
        }
    }
}
