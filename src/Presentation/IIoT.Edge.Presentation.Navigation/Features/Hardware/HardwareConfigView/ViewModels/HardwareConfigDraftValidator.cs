using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

/// <summary>
/// 硬件配置弹窗草稿校验器，集中维护点位地址、数量和数据类型校验规则。
/// </summary>
internal static class HardwareConfigDraftValidator
{
    public static string? ValidateIoMapping(
        IoMappingVm mapping,
        Func<string, string, string> getText)
    {
        if (string.IsNullOrWhiteSpace(mapping.PlcAddress))
        {
            return getText("Navigation_Hardware_Validation_IoAddressRequired", "PLC 地址不能为空。");
        }

        if (mapping.AddressCount <= 0)
        {
            return getText("Navigation_Hardware_Validation_IoAddressCountPositive", "地址数量必须大于 0。");
        }

        if (!IoMappingOptionCatalog.IsKnownDataType(mapping.DataType))
        {
            return getText("Navigation_Hardware_Validation_IoDataTypeRequired", "请选择 IO 数据类型。");
        }

        return null;
    }

    public static string? ValidateInteractionPair(
        IoInteractionPairDraftVm pair,
        bool hasCompleteSourcePair,
        Func<string, string, string> getText)
    {
        if (!hasCompleteSourcePair)
        {
            return getText("Navigation_Hardware_Validation_InteractionGroupIncomplete", "交互组必须同时包含读信号和写信号。");
        }

        if (string.IsNullOrWhiteSpace(pair.ReadPlcAddress) || string.IsNullOrWhiteSpace(pair.WritePlcAddress))
        {
            return getText("Navigation_Hardware_Validation_InteractionAddressRequired", "交互点位 PLC 地址不能为空。");
        }

        if (pair.ReadAddressCount <= 0 || pair.WriteAddressCount <= 0)
        {
            return getText("Navigation_Hardware_Validation_IoAddressCountPositive", "IO 地址数量必须大于 0。");
        }

        if (!IoMappingOptionCatalog.IsKnownDataType(pair.ReadDataType)
            || !IoMappingOptionCatalog.IsKnownDataType(pair.WriteDataType))
        {
            return getText("Navigation_Hardware_Validation_IoDataTypeRequired", "请选择 IO 数据类型。");
        }

        return null;
    }
}
