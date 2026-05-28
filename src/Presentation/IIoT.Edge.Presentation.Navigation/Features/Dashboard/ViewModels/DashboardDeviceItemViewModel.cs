using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Dashboard;

public sealed class DashboardDeviceItemViewModel : BaseNotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _address = string.Empty;
    private string _deviceType = string.Empty;
    private bool _isConnected;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Address
    {
        get => _address;
        set { _address = value; OnPropertyChanged(); }
    }

    public string DeviceType
    {
        get => _deviceType;
        set { _deviceType = value; OnPropertyChanged(); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnPropertyChanged(); }
    }
}
