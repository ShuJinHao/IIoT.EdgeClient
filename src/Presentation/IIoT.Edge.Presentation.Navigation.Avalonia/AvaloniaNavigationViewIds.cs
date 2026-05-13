namespace IIoT.Edge.Presentation.Navigation.Avalonia;

public static class CoreAvaloniaViewIds
{
    public const string Diagnostics = "Core.Diagnostics";
}

public sealed record StandardAvaloniaModuleViewIds(
    string DataView,
    string CapacityView,
    string Monitor,
    string PlcTaskBindingView)
{
    public static StandardAvaloniaModuleViewIds Create(string moduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        var prefix = moduleKey.Trim();
        return new StandardAvaloniaModuleViewIds(
            $"{prefix}.DataView",
            $"{prefix}.CapacityView",
            $"{prefix}.Monitor",
            $"{prefix}.PlcTaskBindingView");
    }
}
