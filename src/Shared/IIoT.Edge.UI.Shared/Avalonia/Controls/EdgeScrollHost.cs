using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeScrollHost : ScrollViewer
{
    public EdgeScrollHost()
    {
        Classes.Add("edge-scroll-host");
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);
}
