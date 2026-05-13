namespace IIoT.Edge.AvaloniaShell.Localization;

public static class ShellLanguageResources
{
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Create()
    {
        return new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-CN"] = new Dictionary<string, string>
            {
                ["Shell_App_Title"] = "边缘客户端 Avalonia Shell",
                ["Shell_Header_Process"] = "工序运行看板",
                ["Shell_Header_Device"] = "设备：压装一线-01",
                ["Shell_Action_Language"] = "EN",
                ["Shell_Action_Dialog"] = "打开弹窗",
                ["Shell_Dialog_Title"] = "确认操作",
                ["Shell_Dialog_Message"] = "这是 Avalonia DialogHost 迁移验证弹窗。",
                ["Shell_Action_Close"] = "关闭",
                ["Shell_Badge_Migration"] = "迁移副本",
                ["Shell_Badge_SampleData"] = "只读演示数据",
                ["Shell_Tab_Monitor"] = "生产监控",
                ["Shell_Tab_IO"] = "IO 调试",
                ["Shell_Tool_Equipment"] = "设备面板",
                ["Shell_Tool_Log"] = "运行日志",
                ["Shell_Monitor_DayShift"] = "白班",
                ["Shell_Monitor_NightShift"] = "夜班",
                ["Shell_Monitor_Total"] = "总数",
                ["Shell_Monitor_Good"] = "良品",
                ["Shell_Monitor_Bad"] = "不良",
                ["Shell_Monitor_Yield"] = "良率",
                ["Shell_Monitor_GridTitle"] = "实时过程数据",
                ["Shell_IO_Title"] = "IO 读写矩阵",
                ["Shell_Column_Signal"] = "PLC 信号",
                ["Shell_Column_Address"] = "地址",
                ["Shell_Column_CurrentReply"] = "当前应答",
                ["Shell_Column_Write"] = "写入操作",
                ["Shell_Button_Write"] = "写入",
                ["Shell_Equipment_Title"] = "设备状态",
                ["Shell_Column_Status"] = "状态",
                ["Shell_Column_Name"] = "名称",
                ["Shell_Column_Connection"] = "连接",
                ["Shell_Column_Value"] = "当前值",
                ["Shell_Footer_Status"] = "Avalonia 迁移基座已启动",
            },
            ["en-US"] = new Dictionary<string, string>
            {
                ["Shell_App_Title"] = "Edge Client Avalonia Shell",
                ["Shell_Header_Process"] = "Process Runtime Dashboard",
                ["Shell_Header_Device"] = "Device: Press Line 01",
                ["Shell_Action_Language"] = "中",
                ["Shell_Action_Dialog"] = "Open Dialog",
                ["Shell_Dialog_Title"] = "Confirm Operation",
                ["Shell_Dialog_Message"] = "This validates the Avalonia DialogHost migration path.",
                ["Shell_Action_Close"] = "Close",
                ["Shell_Badge_Migration"] = "Migration Copy",
                ["Shell_Badge_SampleData"] = "Read-only Sample Data",
                ["Shell_Tab_Monitor"] = "Monitor",
                ["Shell_Tab_IO"] = "IO Debug",
                ["Shell_Tool_Equipment"] = "Equipment",
                ["Shell_Tool_Log"] = "Runtime Log",
                ["Shell_Monitor_DayShift"] = "Day Shift",
                ["Shell_Monitor_NightShift"] = "Night Shift",
                ["Shell_Monitor_Total"] = "Total",
                ["Shell_Monitor_Good"] = "Good",
                ["Shell_Monitor_Bad"] = "Bad",
                ["Shell_Monitor_Yield"] = "Yield",
                ["Shell_Monitor_GridTitle"] = "Realtime Process Data",
                ["Shell_IO_Title"] = "IO Read/Write Matrix",
                ["Shell_Column_Signal"] = "PLC Signal",
                ["Shell_Column_Address"] = "Address",
                ["Shell_Column_CurrentReply"] = "Current Reply",
                ["Shell_Column_Write"] = "Write",
                ["Shell_Button_Write"] = "Write",
                ["Shell_Equipment_Title"] = "Equipment Status",
                ["Shell_Column_Status"] = "Status",
                ["Shell_Column_Name"] = "Name",
                ["Shell_Column_Connection"] = "Connection",
                ["Shell_Column_Value"] = "Current Value",
                ["Shell_Footer_Status"] = "Avalonia migration shell is running",
            }
        };
    }
}
