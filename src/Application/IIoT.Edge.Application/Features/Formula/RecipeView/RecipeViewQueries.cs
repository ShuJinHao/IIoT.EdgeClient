using IIoT.Edge.SharedKernel.DataPipeline.Recipe;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Recipe;
using MediatR;

namespace IIoT.Edge.Application.Features.Formula.RecipeView;

/// <summary>
/// 配方页面快照。
/// 用于界面展示当前配方、来源状态和参数列表。
/// </summary>
public record RecipeViewSnapshot(
    string RecipeName,
    string RecipeVersion,
    string ProcessName,
    string UpdatedAt,
    bool IsCloudSource,
    List<RecipeParamItemDto> Params);

/// <summary>
/// 查询：获取配方页面快照。
/// </summary>
public record GetRecipeViewSnapshotQuery : IRequest<RecipeViewSnapshot?>;

/// <summary>
/// 查询：判断当前用户是否具备本地管理员权限。
/// </summary>
public record GetIsLocalAdminQuery : IRequest<bool>;

/// <summary>
/// 命令：从云端同步配方。
/// </summary>
public record SyncRecipeFromCloudCommand : IRequest<bool>;

/// <summary>
/// 命令：切换当前配方来源。
/// </summary>
public record SwitchRecipeSourceCommand(RecipeSource Source) : IRequest;

/// <summary>
/// 命令：保存单个本地配方参数。
/// </summary>
public record SaveLocalRecipeParamCommand(string Key, double? Min, double? Max, string Unit) : IRequest;

/// <summary>
/// 命令：删除单个本地配方参数。
/// </summary>
public record DeleteLocalRecipeParamCommand(string Key) : IRequest;

/// <summary>
/// 处理器：组装配方页面快照。
/// </summary>
public class GetRecipeViewSnapshotHandler(IRecipeService recipeService)
    : IRequestHandler<GetRecipeViewSnapshotQuery, RecipeViewSnapshot?>
{
    public Task<RecipeViewSnapshot?> Handle(
        GetRecipeViewSnapshotQuery request,
        CancellationToken cancellationToken)
    {
        var recipe = recipeService.ActiveRecipe;
        if (recipe is null)
        {
            return Task.FromResult<RecipeViewSnapshot?>(null);
        }

        var parameters = recipe.Parameters
            .OrderBy(kv => kv.Key)
            .Select(kv => new RecipeParamItemDto
            {
                Name = kv.Value.Name,
                Min = kv.Value.Min?.ToString("F2") ?? "--",
                Max = kv.Value.Max?.ToString("F2") ?? "--",
                Unit = kv.Value.Unit
            })
            .ToList();

        return Task.FromResult<RecipeViewSnapshot?>(new RecipeViewSnapshot(
            recipe.RecipeName,
            recipe.Version,
            recipe.ProcessName,
            recipe.UpdatedAt,
            recipeService.ActiveSource == RecipeSource.Cloud,
            parameters));
    }
}

/// <summary>
/// 处理器：判断当前用户是否具备本地管理员权限。
/// </summary>
public class GetIsLocalAdminHandler(IAuthService authService)
    : IRequestHandler<GetIsLocalAdminQuery, bool>
{
    public Task<bool> Handle(GetIsLocalAdminQuery request, CancellationToken cancellationToken)
        => Task.FromResult(authService.CurrentUser?.IsLocalAdmin ?? false);
}

/// <summary>
/// 处理器：触发云端配方同步。
/// </summary>
public class SyncRecipeFromCloudHandler(IRecipeService recipeService)
    : IRequestHandler<SyncRecipeFromCloudCommand, bool>
{
    public Task<bool> Handle(SyncRecipeFromCloudCommand request, CancellationToken cancellationToken)
        => recipeService.PullFromCloudAsync();
}

/// <summary>
/// 处理器：切换配方来源。
/// </summary>
public class SwitchRecipeSourceHandler(IRecipeService recipeService)
    : IRequestHandler<SwitchRecipeSourceCommand>
{
    public Task Handle(SwitchRecipeSourceCommand request, CancellationToken cancellationToken)
    {
        recipeService.SwitchSource(request.Source);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 处理器：保存单个本地配方参数。
/// </summary>
public class SaveLocalRecipeParamHandler(IRecipeService recipeService)
    : IRequestHandler<SaveLocalRecipeParamCommand>
{
    public Task Handle(SaveLocalRecipeParamCommand request, CancellationToken cancellationToken)
    {
        recipeService.SetLocalParam(request.Key, request.Min, request.Max, request.Unit);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 处理器：删除单个本地配方参数。
/// </summary>
public class DeleteLocalRecipeParamHandler(IRecipeService recipeService)
    : IRequestHandler<DeleteLocalRecipeParamCommand>
{
    public Task Handle(DeleteLocalRecipeParamCommand request, CancellationToken cancellationToken)
    {
        recipeService.RemoveLocalParam(request.Key);
        return Task.CompletedTask;
    }
}
