using Avalonia.Controls;
using Dock.Model.Mvvm.Core;

namespace IIoT.Edge.AvaloniaPoc.Models;

public sealed class PocDockable : DockableBase
{
    public PocDockable(string id, string title, Control content)
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
