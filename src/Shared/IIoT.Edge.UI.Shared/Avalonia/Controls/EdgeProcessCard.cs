using Avalonia;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 统一工序入口卡片，用于 Launcher 或模块入口展示。
/// </summary>
public class EdgeProcessCard : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EdgeProcessCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<EdgeProcessCard, string?>(nameof(Subtitle));

    public static readonly StyledProperty<string?> DetailProperty =
        AvaloniaProperty.Register<EdgeProcessCard, string?>(nameof(Detail));

    public static readonly StyledProperty<string?> PluginLabelProperty =
        AvaloniaProperty.Register<EdgeProcessCard, string?>(nameof(PluginLabel));

    public static readonly StyledProperty<string?> PluginPathProperty =
        AvaloniaProperty.Register<EdgeProcessCard, string?>(nameof(PluginPath));

    public static readonly StyledProperty<string?> DataLabelProperty =
        AvaloniaProperty.Register<EdgeProcessCard, string?>(nameof(DataLabel));

    public static readonly StyledProperty<string?> DataPathProperty =
        AvaloniaProperty.Register<EdgeProcessCard, string?>(nameof(DataPath));

    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<EdgeProcessCard, object?>(nameof(Icon));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<EdgeProcessCard, object?>(nameof(ActionContent));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public string? PluginLabel
    {
        get => GetValue(PluginLabelProperty);
        set => SetValue(PluginLabelProperty, value);
    }

    public string? PluginPath
    {
        get => GetValue(PluginPathProperty);
        set => SetValue(PluginPathProperty, value);
    }

    public string? DataLabel
    {
        get => GetValue(DataLabelProperty);
        set => SetValue(DataLabelProperty, value);
    }

    public string? DataPath
    {
        get => GetValue(DataPathProperty);
        set => SetValue(DataPathProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }
}
