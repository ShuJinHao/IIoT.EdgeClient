using Avalonia;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 统一功能 Hero 卡片，只承载视觉结构，具体文案由使用方资源传入。
/// </summary>
public class EdgeFeatureHeroCard : TemplatedControl
{
    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<EdgeFeatureHeroCard, object?>(nameof(Icon));

    public static readonly StyledProperty<string?> VersionLabelProperty =
        AvaloniaProperty.Register<EdgeFeatureHeroCard, string?>(nameof(VersionLabel));

    public static readonly StyledProperty<string?> VersionTextProperty =
        AvaloniaProperty.Register<EdgeFeatureHeroCard, string?>(nameof(VersionText));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EdgeFeatureHeroCard, string?>(nameof(Title));

    public static readonly StyledProperty<object?> InfoContentProperty =
        AvaloniaProperty.Register<EdgeFeatureHeroCard, object?>(nameof(InfoContent));

    public static readonly StyledProperty<object?> FlowContentProperty =
        AvaloniaProperty.Register<EdgeFeatureHeroCard, object?>(nameof(FlowContent));

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? VersionLabel
    {
        get => GetValue(VersionLabelProperty);
        set => SetValue(VersionLabelProperty, value);
    }

    public string? VersionText
    {
        get => GetValue(VersionTextProperty);
        set => SetValue(VersionTextProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? InfoContent
    {
        get => GetValue(InfoContentProperty);
        set => SetValue(InfoContentProperty, value);
    }

    public object? FlowContent
    {
        get => GetValue(FlowContentProperty);
        set => SetValue(FlowContentProperty, value);
    }
}
