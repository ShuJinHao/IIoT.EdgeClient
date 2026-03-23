using IIoT.Edge.Common.Mvvm;

namespace IIoT.Edge.Module.Hardware.HardwareConfigView.Models;

public class SerialDeviceVm : BaseNotifyPropertyChanged
{
    private int _id;
    public int Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    private string _deviceName = string.Empty;
    public string DeviceName
    {
        get => _deviceName;
        set { _deviceName = value; OnPropertyChanged(); }
    }

    private string _deviceType = string.Empty;
    public string DeviceType
    {
        get => _deviceType;
        set { _deviceType = value; OnPropertyChanged(); }
    }

    private string _portName = string.Empty;
    public string PortName
    {
        get => _portName;
        set { _portName = value; OnPropertyChanged(); }
    }

    private int _baudRate = 9600;
    public int BaudRate
    {
        get => _baudRate;
        set { _baudRate = value; OnPropertyChanged(); }
    }

    private int _dataBits = 8;
    public int DataBits
    {
        get => _dataBits;
        set { _dataBits = value; OnPropertyChanged(); }
    }

    private string _stopBits = "One";
    public string StopBits
    {
        get => _stopBits;
        set { _stopBits = value; OnPropertyChanged(); }
    }

    private string _parity = "None";
    public string Parity
    {
        get => _parity;
        set { _parity = value; OnPropertyChanged(); }
    }

    private string? _sendCmd1;
    public string? SendCmd1
    {
        get => _sendCmd1;
        set { _sendCmd1 = value; OnPropertyChanged(); }
    }

    private string? _sendCmd2;
    public string? SendCmd2
    {
        get => _sendCmd2;
        set { _sendCmd2 = value; OnPropertyChanged(); }
    }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    private string? _remark;
    public string? Remark
    {
        get => _remark;
        set { _remark = value; OnPropertyChanged(); }
    }
}