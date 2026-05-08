namespace IIoT.Edge.Presentation.Navigation.PluginSystem;

/// <summary>
/// 标准模块页面路由标识生成器，避免每个工序重复维护同一组 ViewId。
/// </summary>
public sealed record StandardModuleViewIds(
    string DataView,
    string CapacityView,
    string Monitor,
    string IoView,
    string RecipeView,
    string ParamView,
    string HardwareConfigView,
    string PlcTaskBindingView)
{
    public static StandardModuleViewIds Create(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        var prefix = moduleKey.Trim();
        return new StandardModuleViewIds(
            $"{prefix}.DataView",
            $"{prefix}.CapacityView",
            $"{prefix}.Monitor",
            $"{prefix}.IOView",
            $"{prefix}.RecipeView",
            $"{prefix}.ParamView",
            $"{prefix}.HardwareConfigView",
            $"{prefix}.PlcTaskBindingView");
    }
}
