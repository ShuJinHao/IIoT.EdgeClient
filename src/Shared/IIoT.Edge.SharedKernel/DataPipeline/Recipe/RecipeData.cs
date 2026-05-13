namespace IIoT.Edge.SharedKernel.DataPipeline.Recipe;

/// <summary>
/// 配方来源类型。
/// </summary>
public enum RecipeSource
{
    Cloud,
    Local
}

/// <summary>
/// 配方内存模型。
/// </summary>
public class RecipeData
{
    public string RecipeId { get; set; } = string.Empty;
    public string RecipeName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;

    /// <summary>
    /// 配方参数列表。
    /// 键为参数名称，例如“电压”，便于快速查找。
    /// </summary>
    public Dictionary<string, RecipeParam> Parameters { get; set; } = new();
}

/// <summary>
/// 单个配方参数。
/// </summary>
public class RecipeParam
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// 本地应急修改时使用的自定义值。
    /// </summary>
    public string? CustomValue { get; set; }

    public override string ToString()
        => $"{Name}: [{Min} ~ {Max}] {Unit}";
}
