using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Abstractions.Tasks;

namespace IIoT.Edge.Infrastructure.Integration.Recipe;

public sealed class RecipeSyncTask : IBackgroundTask
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

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.Info($"[配方同步] 已启动，间隔：{_syncInterval.TotalSeconds:0}s");

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

            await ExecuteOnceAsync().ConfigureAwait(false);
        }

        _logger.Info("[配方同步] 已停止。");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    internal async Task ExecuteOnceAsync()
    {
        if (!_deviceService.CanUploadToCloud || _deviceService.CurrentDevice is null)
        {
            return;
        }

        try
        {
            var synced = await _recipeService.PullFromCloudAsync().ConfigureAwait(false);
            if (synced)
            {
                _logger.Info("[配方同步] 云端配方缓存已刷新。");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[配方同步] 云端配方同步失败：{ex.Message}");
        }
    }
}
