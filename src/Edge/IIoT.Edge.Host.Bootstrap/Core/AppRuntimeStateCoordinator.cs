using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Recipe;

namespace IIoT.Edge.Shell.Core;

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
        List<Exception>? failures = null;
        try
        {
            _contextStore.SaveToFile();
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
            _logger.Error($"[生命周期] 运行上下文保存失败：{ex.Message}");
        }

        try
        {
            _recipeService.SaveToFile();
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
            _logger.Error($"[生命周期] 配方保存失败：{ex.Message}");
        }

        if (failures is { Count: 1 })
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();

        if (failures is { Count: > 1 })
            throw new AggregateException(failures);

        _logger.Info("[生命周期] 关闭前运行时状态已保存。");
        return Task.CompletedTask;
    }
}
