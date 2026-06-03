using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

/// <summary>
/// 新增 IO 点位时可选择的插件标准信号，来源于当前插件库的强类型信号 profile。
/// </summary>
public sealed class IoStandardSignalOptionVm
{
    public IoStandardSignalOptionVm(ModuleIoTemplateEntry template)
    {
        SignalKey = template.SignalKey;
        PlcAddress = template.PlcAddress;
        AddressCount = template.AddressCount;
        DataType = template.DataType;
        Direction = template.Direction;
        SortOrder = template.SortOrder;
        Category = template.Category;
        BusinessGroup = template.BusinessGroup;
        Remark = template.Remark;
    }

    public string SignalKey { get; }

    public string PlcAddress { get; }

    public int AddressCount { get; }

    public string DataType { get; }

    public string Direction { get; }

    public int SortOrder { get; }

    public string Category { get; }

    public string BusinessGroup { get; }

    public string? Remark { get; }

    public string DisplayText
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(BusinessGroup) ? SignalKey : BusinessGroup;
            return $"{title}（{Direction} / {DataType} / {PlcAddress}）";
        }
    }
}
