using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeFilterComboBox : ComboBox
{
    public static readonly StyledProperty<double> DropDownVerticalOffsetProperty =
        AvaloniaProperty.Register<EdgeFilterComboBox, double>(
            nameof(DropDownVerticalOffset),
            14d);

    private Popup? _popup;
    private Control? _popupOffsetFallback;

    static EdgeFilterComboBox()
    {
        DropDownVerticalOffsetProperty.Changed.AddClassHandler<EdgeFilterComboBox>((combo, _) => combo.ApplyDropDownOffset());
    }

    public EdgeFilterComboBox()
    {
        Classes.Add("edge-filter-combo");
    }

    public double DropDownVerticalOffset
    {
        get => GetValue(DropDownVerticalOffsetProperty);
        set => SetValue(DropDownVerticalOffsetProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ComboBox);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _popupOffsetFallback = _popup is null
            ? e.NameScope.Find<Control>("PopupBorder")
              ?? e.NameScope.Find<Control>("PART_PopupBorder")
              ?? e.NameScope.Find<Control>("DropDownBorder")
            : null;

        ApplyDropDownOffset();
    }

    private void ApplyDropDownOffset()
    {
        var offset = Math.Max(0d, DropDownVerticalOffset);

        if (_popup is not null)
        {
            _popup.VerticalOffset = offset;
            return;
        }

        if (_popupOffsetFallback is not null)
        {
            _popupOffsetFallback.RenderTransform = offset > 0d
                ? new TranslateTransform(0d, offset)
                : null;
        }
    }
}
