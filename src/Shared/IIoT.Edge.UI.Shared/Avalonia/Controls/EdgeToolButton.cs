using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeToolButton : Button
{
    public EdgeToolButton()
    {
        Classes.Add("edge-tool-button");
    }

    protected override Type StyleKeyOverride => typeof(Button);
}
