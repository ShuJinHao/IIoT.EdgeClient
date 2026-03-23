using IIoT.Edge.Common.Mvvm;

namespace IIoT.Edge.Module.Hardware.HardwareConfigView.Models;

public class IoMappingVm : BaseNotifyPropertyChanged
{
    private int _id;
    public int Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    private int _networkDeviceId;
    public int NetworkDeviceId
    {
        get => _networkDeviceId;
        set { _networkDeviceId = value; OnPropertyChanged(); }
    }

    private string _label = string.Empty;
    public string Label
    {
        get => _label;
        set { _label = value; OnPropertyChanged(); }
    }

    private string _plcAddress = string.Empty;
    public string PlcAddress
    {
        get => _plcAddress;
        set { _plcAddress = value; OnPropertyChanged(); }
    }

    private int _addressCount = 1;
    public int AddressCount
    {
        get => _addressCount;
        set { _addressCount = value; OnPropertyChanged(); }
    }

    private string _dataType = "Int16";
    public string DataType
    {
        get => _dataType;
        set { _dataType = value; OnPropertyChanged(); }
    }

    private string _direction = "Read";
    public string Direction
    {
        get => _direction;
        set { _direction = value; OnPropertyChanged(); }
    }

    private int _sortOrder;
    public int SortOrder
    {
        get => _sortOrder;
        set { _sortOrder = value; OnPropertyChanged(); }
    }

    private string? _remark;
    public string? Remark
    {
        get => _remark;
        set { _remark = value; OnPropertyChanged(); }
    }
}