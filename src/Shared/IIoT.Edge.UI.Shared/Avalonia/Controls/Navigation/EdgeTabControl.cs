using Avalonia.Controls;
using System;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 统一页签控件，保留 Avalonia TabControl 行为，只收敛共享视觉。
/// </summary>
public class EdgeTabControl : TabControl
{
    public EdgeTabControl()
    {
        Classes.Add("edge-tabs");
    }

    protected override Type StyleKeyOverride => typeof(TabControl);
}
