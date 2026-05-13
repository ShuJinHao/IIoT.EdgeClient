using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

/// <summary>
/// 新增 IO 点位弹窗的临时编辑模型，确认前不写入主表。
/// </summary>
public sealed class IoMappingDraftVm : BaseNotifyPropertyChanged
{
    private string _source = IoMappingOptionCatalog.PointSourceCustomDebug;
    public string Source
    {
        get => _source;
        set
        {
            _source = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStandardSource));
            OnPropertyChanged(nameof(IsCustomSource));
        }
    }

    public bool IsStandardSource
        => string.Equals(Source, IoMappingOptionCatalog.PointSourceStandardSignal, StringComparison.OrdinalIgnoreCase);

    public bool IsCustomSource
        => string.Equals(Source, IoMappingOptionCatalog.PointSourceCustomDebug, StringComparison.OrdinalIgnoreCase);

    private string _category = IoMappingOptionCatalog.CategorySingleRead;
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
            ApplyCategoryRules();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAddressCountEditable));
        }
    }

    private string _direction = IoMappingOptionCatalog.DirectionRead;
    public string Direction
    {
        get => _direction;
        set { _direction = value; OnPropertyChanged(); }
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
        }
    }

    private string _dataType = IoMappingOptionCatalog.DataTypeInt16;
    public string DataType
    {
        get => _dataType;
        set { _dataType = value; OnPropertyChanged(); }
    }

    private string _businessGroup = string.Empty;
    public string BusinessGroup
    {
        get => _businessGroup;
        set { _businessGroup = value; OnPropertyChanged(); }
    }

    private string _signalName = string.Empty;
    public string SignalName
    {
        get => _signalName;
        set { _signalName = value; OnPropertyChanged(); }
    }

    private string? _remark;
    public string? Remark
    {
        get => _remark;
        set { _remark = value; OnPropertyChanged(); }
    }

    public bool IsAddressCountEditable
        => IoMappingOptionCatalog.CanEditAddressCount(Category);

    private void ApplyCategoryRules()
    {
        var direction = IoMappingOptionCatalog.GetDirectionForCategory(_category);
        if (!string.IsNullOrWhiteSpace(direction) && !string.Equals(_direction, direction, StringComparison.OrdinalIgnoreCase))
        {
            _direction = direction;
            OnPropertyChanged(nameof(Direction));
        }

        var normalizedCount = IoMappingOptionCatalog.NormalizeAddressCount(_category, _addressCount);
        if (_addressCount != normalizedCount)
        {
            _addressCount = normalizedCount;
            OnPropertyChanged(nameof(AddressCount));
        }
    }
}
