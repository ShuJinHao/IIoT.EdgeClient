using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeFilterComboBox : ComboBox
{
    public EdgeFilterComboBox()
    {
        Classes.Add("edge-filter-combo");
    }

    protected override Type StyleKeyOverride => typeof(ComboBox);
}
