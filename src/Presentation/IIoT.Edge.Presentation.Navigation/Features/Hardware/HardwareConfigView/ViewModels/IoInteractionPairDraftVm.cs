using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

/// <summary>
/// 新增信号交互弹窗的临时配对模型，一次生成同一标准业务动作下的一读一写。
/// </summary>
public sealed class IoInteractionPairDraftVm : BaseNotifyPropertyChanged
{
    private string _businessGroup = string.Empty;
    public string BusinessGroup
    {
        get => _businessGroup;
        set { _businessGroup = value; OnPropertyChanged(); }
    }

    private string _readPlcAddress = string.Empty;
    public string ReadPlcAddress
    {
        get => _readPlcAddress;
        set { _readPlcAddress = value; OnPropertyChanged(); }
    }

    private int _readAddressCount = 1;
    public int ReadAddressCount
    {
        get => _readAddressCount;
        set
        {
            var normalized = IoMappingOptionCatalog.NormalizeAddressCount(
                IoMappingOptionCatalog.CategoryInteraction,
                value);
            if (_readAddressCount == normalized)
            {
                return;
            }

            _readAddressCount = normalized;
            OnPropertyChanged();
        }
    }

    private string _readDataType = IoMappingOptionCatalog.DataTypeInt16;
    public string ReadDataType
    {
        get => _readDataType;
        set { _readDataType = value; OnPropertyChanged(); }
    }

    private string _writePlcAddress = string.Empty;
    public string WritePlcAddress
    {
        get => _writePlcAddress;
        set { _writePlcAddress = value; OnPropertyChanged(); }
    }

    private int _writeAddressCount = 1;
    public int WriteAddressCount
    {
        get => _writeAddressCount;
        set
        {
            var normalized = IoMappingOptionCatalog.NormalizeAddressCount(
                IoMappingOptionCatalog.CategoryInteraction,
                value);
            if (_writeAddressCount == normalized)
            {
                return;
            }

            _writeAddressCount = normalized;
            OnPropertyChanged();
        }
    }

    private string _writeDataType = IoMappingOptionCatalog.DataTypeInt16;
    public string WriteDataType
    {
        get => _writeDataType;
        set { _writeDataType = value; OnPropertyChanged(); }
    }

    private string? _remark;
    public string? Remark
    {
        get => _remark;
        set { _remark = value; OnPropertyChanged(); }
    }
}
