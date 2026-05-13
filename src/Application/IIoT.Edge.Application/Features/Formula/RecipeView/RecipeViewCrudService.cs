using IIoT.Edge.SharedKernel.DataPipeline.Recipe;
using MediatR;

namespace IIoT.Edge.Application.Features.Formula.RecipeView;

/// <summary>
/// 配方页面增删改查服务契约。
/// 负责配方快照读取、本地权限判断、云端同步和本地参数维护。
/// </summary>
public interface IRecipeViewCrudService
{
    Task<RecipeViewSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<bool> GetIsLocalAdminAsync(CancellationToken cancellationToken = default);

    Task<bool> SyncCloudAsync(CancellationToken cancellationToken = default);

    Task SwitchSourceAsync(RecipeSource source, CancellationToken cancellationToken = default);

    Task SaveLocalParamAsync(string key, double? min, double? max, string unit, CancellationToken cancellationToken = default);

    Task DeleteLocalParamAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// 配方页面增删改查服务。
/// 负责将界面操作转发到对应的配方查询与命令处理器。
/// </summary>
public sealed class RecipeViewCrudService(ISender sender) : IRecipeViewCrudService
{
    public Task<RecipeViewSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => sender.Send(new GetRecipeViewSnapshotQuery(), cancellationToken);

    public Task<bool> GetIsLocalAdminAsync(CancellationToken cancellationToken = default)
        => sender.Send(new GetIsLocalAdminQuery(), cancellationToken);

    public Task<bool> SyncCloudAsync(CancellationToken cancellationToken = default)
        => sender.Send(new SyncRecipeFromCloudCommand(), cancellationToken);

    public Task SwitchSourceAsync(RecipeSource source, CancellationToken cancellationToken = default)
        => sender.Send(new SwitchRecipeSourceCommand(source), cancellationToken);

    public Task SaveLocalParamAsync(
        string key,
        double? min,
        double? max,
        string unit,
        CancellationToken cancellationToken = default)
        => sender.Send(new SaveLocalRecipeParamCommand(key, min, max, unit), cancellationToken);

    public Task DeleteLocalParamAsync(string key, CancellationToken cancellationToken = default)
        => sender.Send(new DeleteLocalRecipeParamCommand(key), cancellationToken);
}
