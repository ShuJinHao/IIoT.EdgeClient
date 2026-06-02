using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 统一状态标签，用于在线、运行、失败、缓存等短状态文本。
/// </summary>
public class EdgeStatusChip : TemplatedControl
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
        AvaloniaProperty.Register<EdgeStatusChip, EdgeVisualStatus>(nameof(Status), EdgeVisualStatus.Info);

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<EdgeStatusChip, string?>(nameof(Text));

    public static readonly StyledProperty<EdgeVisualVariant> VariantProperty =
        AvaloniaProperty.Register<EdgeStatusChip, EdgeVisualVariant>(nameof(Variant), EdgeVisualVariant.Default);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<EdgeStatusChip, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<EdgeStatusChip, IBrush?>(nameof(TextBrush));

    public static readonly StyledProperty<Thickness> ChipPaddingProperty =
        AvaloniaProperty.Register<EdgeStatusChip, Thickness>(nameof(ChipPadding), new Thickness(8, 2));

    static EdgeStatusChip()
    {
        StatusProperty.Changed.AddClassHandler<EdgeStatusChip>((control, _) => control.UpdateStatusClass());
    }

    public EdgeStatusChip()
    {
        UpdateStatusClass();
    }

    public EdgeVisualStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public EdgeVisualVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public Thickness ChipPadding
    {
        get => GetValue(ChipPaddingProperty);
        set => SetValue(ChipPaddingProperty, value);
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
