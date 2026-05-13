using IIoT.Edge.SharedKernel.DataPipeline.Recipe;

namespace IIoT.Edge.Application.Abstractions.Recipe;

/// <summary>
/// 配方服务契约。
/// 负责配方来源切换、参数读取、云端同步、本地应急维护与变更通知。
/// </summary>
public interface IRecipeService
{
    // 当前数据源。

    RecipeSource ActiveSource { get; }
    void SwitchSource(RecipeSource source);

    // 读取能力，供任务层和页面查询使用。

    /// <summary>
    /// 按参数名称获取配方参数，例如“电压”。
    /// </summary>
    RecipeParam? GetParam(string name);

    /// <summary>
    /// 获取全部配方参数。
    /// </summary>
    IReadOnlyDictionary<string, RecipeParam> GetAllParams();

    // 当前配方元信息。

    RecipeData? ActiveRecipe { get; }
    RecipeData? CloudRecipe { get; }
    RecipeData? LocalRecipe { get; }

    // 云端同步能力。

    Task<bool> PullFromCloudAsync();

    // 本地应急维护能力。

    /// <summary>
    /// 新增或更新本地配方参数。
    /// </summary>
    void SetLocalParam(string name, double? min, double? max, string unit);

    /// <summary>
    /// 删除本地配方参数。
    /// </summary>
    void RemoveLocalParam(string name);

    // 本地持久化能力。

    void LoadFromFile();
    void SaveToFile();

    // 配方变更通知。

    event Action? RecipeChanged;
}
