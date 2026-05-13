using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IIoT.Edge.Application.Common.Models;

public abstract class ObservableModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
