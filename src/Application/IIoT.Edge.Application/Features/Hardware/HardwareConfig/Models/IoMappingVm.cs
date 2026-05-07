using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;

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

    private string _signalKey = string.Empty;
    public string SignalKey
    {
        get => _signalKey;
        set { _signalKey = value; OnPropertyChanged(); }
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
        set
        {
            var normalized = IoMappingOptionCatalog.NormalizeAddressCount(Category, value);
            if (_addressCount == normalized)
            {
                return;
            }

            _addressCount = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GroupTitle));
        }
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
        set
        {
            if (_direction == value)
            {
                return;
            }

            _direction = value;
            OnPropertyChanged();
        }
    }

    private string _category = string.Empty;
    public string Category
    {
        get => _category;
        set
        {
            var normalized = IoMappingOptionCatalog.NormalizeCategory(value, _addressCount);
            if (_category == normalized)
            {
                return;
            }

            _category = normalized;
            var derivedDirection = IoMappingOptionCatalog.GetDirectionForCategory(_category);
            if (!string.IsNullOrWhiteSpace(derivedDirection))
            {
                Direction = derivedDirection;
            }

            var normalizedAddressCount = IoMappingOptionCatalog.NormalizeAddressCount(_category, _addressCount);
            if (_addressCount != normalizedAddressCount)
            {
                _addressCount = normalizedAddressCount;
                OnPropertyChanged(nameof(AddressCount));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(GroupTitle));
            OnPropertyChanged(nameof(IsAddressCountEditable));
        }
    }

    private string _businessGroup = string.Empty;
    public string BusinessGroup
    {
        get => _businessGroup;
        set
        {
            _businessGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GroupTitle));
        }
    }

    private string _signalName = string.Empty;
    public string SignalName
    {
        get => _signalName;
        set { _signalName = value; OnPropertyChanged(); }
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

    /// <summary>
    /// IO 映射统一分组标题，硬件配置页和 IO 交互页共用同一分类规则。
    /// </summary>
    public string GroupTitle
    {
        get
        {
            var category = IoMappingDisplay.ResolveCategory(Category, AddressCount);
            return IoMappingDisplay.BuildSectionTitle(
                category,
                IoMappingDisplay.ResolveBusinessGroup(BusinessGroup, category));
        }
    }

    /// <summary>
    /// 硬件配置表格中地址数量是否允许现场修改；信号交互和单点读写固定为 1。
    /// </summary>
    public bool IsAddressCountEditable
        => IoMappingOptionCatalog.CanEditAddressCount(Category);
}
