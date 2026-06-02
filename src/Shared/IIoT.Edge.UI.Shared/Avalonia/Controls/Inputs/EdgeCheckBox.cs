using Avalonia.Controls;
using System;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeCheckBox : CheckBox
{
    public EdgeCheckBox()
    {
        Classes.Add("edge-check-box");
    }

    protected override Type StyleKeyOverride => typeof(CheckBox);
}
