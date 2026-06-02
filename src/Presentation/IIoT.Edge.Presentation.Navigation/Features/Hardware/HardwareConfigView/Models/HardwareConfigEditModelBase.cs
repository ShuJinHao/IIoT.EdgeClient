using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;

public abstract class HardwareConfigEditModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
