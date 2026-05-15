using CommunityToolkit.Mvvm.ComponentModel;

namespace IIoT.Edge.UI.Avalonia.Mvvm;

public abstract class AvaloniaViewModelBase : ObservableObject
{
    public virtual string ViewId => GetType().Name;

    public virtual string ViewTitle => ViewId;

    public virtual Task OnActivatedAsync()
    {
        return Task.CompletedTask;
    }

    public virtual Task OnDeactivatedAsync()
    {
        return Task.CompletedTask;
    }
}
