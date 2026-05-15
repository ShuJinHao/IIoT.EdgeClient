using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Recipe;

namespace IIoT.Edge.Host.Bootstrap;

public interface IAppRuntimeStateCoordinator
{
    Task RestoreAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
}

public sealed class AppRuntimeStateCoordinator : IAppRuntimeStateCoordinator
{
    private readonly IProductionContextStore _contextStore;
    private readonly IRecipeService _recipeService;
    private readonly IDevelopmentSampleInitializer _developmentSampleInitializer;
    private readonly ILogService _logger;

    public AppRuntimeStateCoordinator(
        IProductionContextStore contextStore,
        IRecipeService recipeService,
        IDevelopmentSampleInitializer developmentSampleInitializer,
        ILogService logger)
    {
        _contextStore = contextStore;
        _recipeService = recipeService;
        _developmentSampleInitializer = developmentSampleInitializer;
        _logger = logger;
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        _contextStore.LoadFromFile();
        _recipeService.LoadFromFile();
        await _developmentSampleInitializer.EnsureRuntimeSamplesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info("[生命周期] 运行时持久化状态恢复完成。");
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        _contextStore.SaveToFile();
        _recipeService.SaveToFile();
        _logger.Info("[生命周期] 关闭前运行时状态已保存。");
        return Task.CompletedTask;
    }
}
