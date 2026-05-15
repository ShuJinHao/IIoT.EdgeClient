using Avalonia.Controls;
using Dock.Model.Mvvm.Core;

namespace IIoT.Edge.UI.Avalonia.Docking;

public sealed class AvaloniaDockable : DockableBase
{
    public AvaloniaDockable(string id, string title, Control content)
    {
        Id = id;
        Title = title;
        Content = content;
        Context = content.DataContext ?? content;
        CanClose = false;
        CanFloat = true;
        CanPin = true;
        CanDrag = true;
        CanDrop = true;
    }

    public Control Content { get; }
}
