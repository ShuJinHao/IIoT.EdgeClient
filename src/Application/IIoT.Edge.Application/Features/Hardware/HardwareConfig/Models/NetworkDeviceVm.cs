using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.Application.Common.Models;

namespace IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;

/// <summary>
/// 网络设备编辑项视图模型。
/// </summary>
public class NetworkDeviceVm : ObservableModelBase
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

    private DeviceType _deviceType;
    public DeviceType DeviceType
    {
        get => _deviceType;
        set
        {
            _deviceType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvailableModels));
            DeviceModel = null;
        }
    }

    private string? _deviceModel;
    public string? DeviceModel
    {
        get => _deviceModel;
        set { _deviceModel = value; OnPropertyChanged(); }
    }

    private string _moduleId = string.Empty;
    public string ModuleId
    {
        get => _moduleId;
        set { _moduleId = value?.Trim() ?? string.Empty; OnPropertyChanged(); }
    }

    public IEnumerable<string> AvailableModels => DeviceType switch
    {
        DeviceType.PLC => Enum.GetNames<PlcType>(),
        DeviceType.Scanner => new[] { "Keyence", "Cognex" },
        DeviceType.Camera => new[] { "HikVision", "Daheng" },
        DeviceType.Tester => new[] { "Other" },
        _ => Array.Empty<string>()
    };

    public static IEnumerable<DeviceType> DeviceTypes
        => Enum.GetValues<DeviceType>();

    private string _ipAddress = string.Empty;
    public string IpAddress
    {
        get => _ipAddress;
        set { _ipAddress = value; OnPropertyChanged(); }
    }

    private int _port1;
    public int Port1
    {
        get => _port1;
        set { _port1 = value; OnPropertyChanged(); }
    }

    private int? _port2;
    public int? Port2
    {
        get => _port2;
        set { _port2 = value; OnPropertyChanged(); }
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

    private int _connectTimeout = 3000;
    public int ConnectTimeout
    {
        get => _connectTimeout;
        set { _connectTimeout = value; OnPropertyChanged(); }
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

