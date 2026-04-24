using IIoT.Edge.Application.Common.Models;

namespace IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;

/// <summary>
/// IO 映射编辑项视图模型。
/// </summary>
public class IoMappingVm : ObservableModelBase
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

    private string _category = "单点读数据";
    public string Category
    {
        get => _category;
        set { _category = value; OnPropertyChanged(); }
    }

    private string _groupName = string.Empty;
    public string GroupName
    {
        get => _groupName;
        set { _groupName = value; OnPropertyChanged(); }
    }

    private string _displayRole = string.Empty;
    public string DisplayRole
    {
        get => _displayRole;
        set { _displayRole = value; OnPropertyChanged(); }
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
