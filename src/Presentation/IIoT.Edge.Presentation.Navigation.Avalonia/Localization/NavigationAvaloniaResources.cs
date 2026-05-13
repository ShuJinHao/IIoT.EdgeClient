using IIoT.Edge.UI.Avalonia.Localization;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Localization;

public sealed class NavigationAvaloniaZhCnResources : IAvaloniaResourceContributor
{
    public string CultureName => "zh-CN";

    public IReadOnlyDictionary<string, string> GetResources()
        => new Dictionary<string, string>
        {
            ["Navigation_Menu_Data"] = "生产数据",
            ["Navigation_Menu_Capacity"] = "产能",
            ["Navigation_Menu_Monitor"] = "监控",
            ["Navigation_Menu_PlcTaskBinding"] = "PLC 任务",
            ["Navigation_Menu_CoreDiagnostics"] = "系统诊断",
            ["Navigation_Column_Time"] = "时间",
            ["Navigation_Column_BatchNo"] = "批次",
            ["Navigation_Column_Total"] = "总数",
            ["Navigation_Column_Ok"] = "良品",
            ["Navigation_Column_Ng"] = "不良",
            ["Navigation_Column_Yield"] = "良率",
            ["Navigation_Column_DeviceName"] = "设备",
            ["Navigation_Column_Status"] = "状态",
            ["Navigation_Column_Message"] = "信息",
            ["Navigation_Column_TaskKey"] = "任务 Key",
            ["Navigation_Column_Signal"] = "信号",
            ["Navigation_Column_Binding"] = "绑定",
            ["Navigation_Button_Query"] = "查询",
            ["Navigation_Button_Export"] = "导出",
            ["Navigation_Diagnostics_RuntimeNotStarted"] = "本批只验证 UI 注册和页面加载，后台运行链路未启动。"
        };
}

public sealed class NavigationAvaloniaEnUsResources : IAvaloniaResourceContributor
{
    public string CultureName => "en-US";

    public IReadOnlyDictionary<string, string> GetResources()
        => new Dictionary<string, string>
        {
            ["Navigation_Menu_Data"] = "Data",
            ["Navigation_Menu_Capacity"] = "Capacity",
            ["Navigation_Menu_Monitor"] = "Monitor",
            ["Navigation_Menu_PlcTaskBinding"] = "PLC Tasks",
            ["Navigation_Menu_CoreDiagnostics"] = "Diagnostics",
            ["Navigation_Column_Time"] = "Time",
            ["Navigation_Column_BatchNo"] = "Batch",
            ["Navigation_Column_Total"] = "Total",
            ["Navigation_Column_Ok"] = "OK",
            ["Navigation_Column_Ng"] = "NG",
            ["Navigation_Column_Yield"] = "Yield",
            ["Navigation_Column_DeviceName"] = "Device",
            ["Navigation_Column_Status"] = "Status",
            ["Navigation_Column_Message"] = "Message",
            ["Navigation_Column_TaskKey"] = "Task Key",
            ["Navigation_Column_Signal"] = "Signal",
            ["Navigation_Column_Binding"] = "Binding",
            ["Navigation_Button_Query"] = "Query",
            ["Navigation_Button_Export"] = "Export",
            ["Navigation_Diagnostics_RuntimeNotStarted"] = "This batch validates UI registration and page loading only; backend runtime is not started."
        };
}
