namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 插件枚举成员上的轻量参数声明，宿主用它生成参数页面和默认值。
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ModuleParamAttribute(ParamValueKind valueKind) : Attribute
{
    public ParamValueKind ValueKind { get; } = valueKind;

    public string? DefaultValue { get; init; }

    public string? Unit { get; init; }

    public string? MinValue { get; init; }

    public string? MaxValue { get; init; }

    public ModuleParamRole Role { get; init; } = ModuleParamRole.None;

    public string? DisplayNameResourceKey { get; init; }

    public string? DisplayNameFallback { get; init; }

    public string? DescriptionResourceKey { get; init; }

    public string? DescriptionFallback { get; init; }
}
