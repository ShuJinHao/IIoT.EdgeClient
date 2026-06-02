using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IIoT.Edge.Presentation.Navigation.Common;

public abstract class PresentationObservableModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
