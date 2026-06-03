using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public enum EdgeScrollHostVariant
{
    Page,
    Dialog,
    Panel
}

public class EdgeScrollHost : ScrollViewer
{
    public static readonly StyledProperty<EdgeScrollHostVariant> VariantProperty =
        AvaloniaProperty.Register<EdgeScrollHost, EdgeScrollHostVariant>(
            nameof(Variant),
            EdgeScrollHostVariant.Page);

    static EdgeScrollHost()
    {
        VariantProperty.Changed.AddClassHandler<EdgeScrollHost>((host, _) => host.UpdateVariantClasses());
    }

    public EdgeScrollHost()
    {
        Classes.Add("edge-scroll-host");
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        UpdateVariantClasses();
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public EdgeScrollHostVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    private void UpdateVariantClasses()
    {
        SetClass("variant-page", Variant == EdgeScrollHostVariant.Page);
        SetClass("variant-dialog", Variant == EdgeScrollHostVariant.Dialog);
        SetClass("variant-panel", Variant == EdgeScrollHostVariant.Panel);
    }

    private void SetClass(string name, bool enabled)
    {
        if (enabled)
        {
            Classes.Add(name);
        }
        else
        {
            Classes.Remove(name);
        }
    }
}
