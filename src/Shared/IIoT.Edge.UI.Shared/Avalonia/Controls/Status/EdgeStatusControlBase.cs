using Avalonia;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 状态类控件基座，统一维护 EdgeVisualStatus 到样式 class 的映射。
/// </summary>
public abstract class EdgeStatusControlBase : TemplatedControl
{
    private static readonly string[] StatusClasses =
    [
        "default",
        "running",
        "idle",
        "stopped",
        "offline",
        "info",
        "cache",
        "warning",
        "error"
    ];

    public static readonly StyledProperty<EdgeVisualStatus> StatusProperty =
        AvaloniaProperty.Register<EdgeStatusControlBase, EdgeVisualStatus>(
            nameof(Status),
            EdgeVisualStatus.Default);

    static EdgeStatusControlBase()
    {
        StatusProperty.Changed.AddClassHandler<EdgeStatusControlBase>((control, _) => control.UpdateStatusClass());
    }

    protected EdgeStatusControlBase()
    {
        UpdateStatusClass();
    }

    public EdgeVisualStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private void UpdateStatusClass()
    {
        foreach (var className in StatusClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(Status.ToString().ToLowerInvariant());
    }
}
