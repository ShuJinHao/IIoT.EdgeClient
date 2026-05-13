namespace IIoT.Edge.Application.Features.Formula.RecipeView;

/// <summary>
/// 配方参数展示项的数据传输对象。
/// </summary>
public sealed class RecipeParamItemDto
{
    public string Name { get; set; } = string.Empty;
    public string Min { get; set; } = string.Empty;
    public string Max { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}
