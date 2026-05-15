using IIoT.Edge.UI.Avalonia.Localization;

namespace IIoT.Edge.Module.Homogenization.Avalonia.Localization;

public sealed class HomogenizationAvaloniaZhCnResources : IAvaloniaResourceContributor
{
    public string CultureName => "zh-CN";

    public IReadOnlyDictionary<string, string> GetResources()
        => new Dictionary<string, string>
        {
            ["Homogenization_DisplayName"] = "匀浆",
            ["Homogenization_Menu_Data"] = "数据",
            ["Homogenization_Title_Data"] = "匀浆出料数据",
            ["Homogenization_Empty_OutboundRecords"] = "暂无匀浆出料记录。",
            ["Homogenization_RecordCountFormat"] = "共 {0} 条出料记录。",
            ["Homogenization_Column_TrayCode"] = "托盘码",
            ["Homogenization_Column_InboundTime"] = "进站时间",
            ["Homogenization_Column_OutboundTime"] = "出料时间",
            ["Homogenization_Column_Status"] = "运行状态",
            ["Homogenization_Column_StirringSpeed"] = "搅拌转速",
            ["Homogenization_Column_Temperature"] = "温度",
            ["Homogenization_Column_Vacuum"] = "真空度",
            ["Homogenization_Column_CntActual"] = "CNT 实际值",
            ["Homogenization_Column_NmpActual"] = "NMP 实际值"
        };
}

public sealed class HomogenizationAvaloniaEnUsResources : IAvaloniaResourceContributor
{
    public string CultureName => "en-US";

    public IReadOnlyDictionary<string, string> GetResources()
        => new Dictionary<string, string>
        {
            ["Homogenization_DisplayName"] = "Homogenization",
            ["Homogenization_Menu_Data"] = "Data",
            ["Homogenization_Title_Data"] = "Homogenization Outbound Data",
            ["Homogenization_Empty_OutboundRecords"] = "No homogenization outbound records.",
            ["Homogenization_RecordCountFormat"] = "{0} outbound records.",
            ["Homogenization_Column_TrayCode"] = "Pallet Code",
            ["Homogenization_Column_InboundTime"] = "Inbound Time",
            ["Homogenization_Column_OutboundTime"] = "Outbound Time",
            ["Homogenization_Column_Status"] = "Status",
            ["Homogenization_Column_StirringSpeed"] = "Stirring Speed",
            ["Homogenization_Column_Temperature"] = "Temperature",
            ["Homogenization_Column_Vacuum"] = "Vacuum",
            ["Homogenization_Column_CntActual"] = "CNT Actual",
            ["Homogenization_Column_NmpActual"] = "NMP Actual"
        };
}
