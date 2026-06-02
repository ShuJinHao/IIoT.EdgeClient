using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public enum EdgeActionButtonKind
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

public enum EdgeActionButtonRole
{
    Standard,
    Cell,
    Nav,
    Language,
    RightRail
}

public enum EdgeActionButtonSize
{
    Normal,
    Compact,
    Icon
}

public enum EdgeActionButtonIconPlacement
{
    Left,
    Right
}

/// <summary>
/// 统一操作按钮。Kind 表达操作语义，Role 表达按钮使用场景。
/// </summary>
public class EdgeActionButton : Button
{
    private static readonly string[] KindClasses =
    [
        "primary",
        "secondary",
        "ghost",
        "danger"
    ];

    private static readonly string[] RoleClasses =
    [
        "role-standard",
        "role-cell",
        "role-nav",
        "role-language",
        "role-right-rail"
    ];

    private static readonly string[] SizeClasses =
    [
        "size-normal",
        "size-compact",
        "size-icon"
    ];

    public static readonly StyledProperty<EdgeActionButtonKind> KindProperty =
        AvaloniaProperty.Register<EdgeActionButton, EdgeActionButtonKind>(nameof(Kind), EdgeActionButtonKind.Primary);

    public static readonly StyledProperty<EdgeActionButtonRole> RoleProperty =
        AvaloniaProperty.Register<EdgeActionButton, EdgeActionButtonRole>(nameof(Role), EdgeActionButtonRole.Standard);

    public static readonly StyledProperty<EdgeActionButtonSize> SizeProperty =
        AvaloniaProperty.Register<EdgeActionButton, EdgeActionButtonSize>(nameof(Size), EdgeActionButtonSize.Normal);

    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<EdgeActionButton, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<EdgeActionButtonIconPlacement> IconPlacementProperty =
        AvaloniaProperty.Register<EdgeActionButton, EdgeActionButtonIconPlacement>(nameof(IconPlacement), EdgeActionButtonIconPlacement.Left);

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<EdgeActionButton, double>(nameof(IconSize), 12);

    public static readonly StyledProperty<double> IconStrokeThicknessProperty =
        AvaloniaProperty.Register<EdgeActionButton, double>(nameof(IconStrokeThickness), 1.45);

    public static readonly DirectProperty<EdgeActionButton, bool> HasLeadingIconProperty =
        AvaloniaProperty.RegisterDirect<EdgeActionButton, bool>(nameof(HasLeadingIcon), button => button.HasLeadingIcon);

    public static readonly DirectProperty<EdgeActionButton, bool> HasTrailingIconProperty =
        AvaloniaProperty.RegisterDirect<EdgeActionButton, bool>(nameof(HasTrailingIcon), button => button.HasTrailingIcon);

    public static readonly DirectProperty<EdgeActionButton, bool> HasContentProperty =
        AvaloniaProperty.RegisterDirect<EdgeActionButton, bool>(nameof(HasContent), button => button.HasContent);

    private bool _hasLeadingIcon;
    private bool _hasTrailingIcon;
    private bool _hasContent;

    static EdgeActionButton()
    {
        KindProperty.Changed.AddClassHandler<EdgeActionButton>((control, _) => control.UpdateKindClass());
        RoleProperty.Changed.AddClassHandler<EdgeActionButton>((control, _) => control.UpdateRoleClass());
        SizeProperty.Changed.AddClassHandler<EdgeActionButton>((control, _) => control.UpdateSizeClass());
        IconProperty.Changed.AddClassHandler<EdgeActionButton>((control, _) => control.UpdateVisualState());
        IconPlacementProperty.Changed.AddClassHandler<EdgeActionButton>((control, _) => control.UpdateVisualState());
        ContentProperty.Changed.AddClassHandler<EdgeActionButton>((control, _) => control.UpdateVisualState());
    }

    public EdgeActionButton()
    {
        UpdateKindClass();
        UpdateRoleClass();
        UpdateSizeClass();
        UpdateVisualState();
    }

    public EdgeActionButtonKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public EdgeActionButtonRole Role
    {
        get => GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    public EdgeActionButtonSize Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public EdgeActionButtonIconPlacement IconPlacement
    {
        get => GetValue(IconPlacementProperty);
        set => SetValue(IconPlacementProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public double IconStrokeThickness
    {
        get => GetValue(IconStrokeThicknessProperty);
        set => SetValue(IconStrokeThicknessProperty, value);
    }

    public bool HasLeadingIcon
    {
        get => _hasLeadingIcon;
        private set => SetAndRaise(HasLeadingIconProperty, ref _hasLeadingIcon, value);
    }

    public bool HasTrailingIcon
    {
        get => _hasTrailingIcon;
        private set => SetAndRaise(HasTrailingIconProperty, ref _hasTrailingIcon, value);
    }

    public bool HasContent
    {
        get => _hasContent;
        private set => SetAndRaise(HasContentProperty, ref _hasContent, value);
    }

    private void UpdateKindClass()
    {
        foreach (var className in KindClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(Kind.ToString().ToLowerInvariant());
    }

    private void UpdateRoleClass()
    {
        foreach (var className in RoleClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(GetRoleClass(Role));
    }

    private void UpdateSizeClass()
    {
        foreach (var className in SizeClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add($"size-{Size.ToString().ToLowerInvariant()}");
    }

    private void UpdateVisualState()
    {
        HasLeadingIcon = Icon is not null && IconPlacement == EdgeActionButtonIconPlacement.Left;
        HasTrailingIcon = Icon is not null && IconPlacement == EdgeActionButtonIconPlacement.Right;
        HasContent = Content is not null;
    }

    private static string GetRoleClass(EdgeActionButtonRole role) =>
        role switch
        {
            EdgeActionButtonRole.RightRail => "role-right-rail",
            _ => $"role-{role.ToString().ToLowerInvariant()}"
        };
}
