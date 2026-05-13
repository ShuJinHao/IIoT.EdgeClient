using Avalonia;
using Avalonia.Controls;

namespace IIoT.Edge.AvaloniaPoc.Services;

public sealed class AvaloniaAppLanguageService : IAppLanguageService
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Resources =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-CN"] = new Dictionary<string, string>
            {
                ["Poc_App_Title"] = "边缘客户端 Avalonia PoC",
                ["Poc_Header_Process"] = "工序运行看板",
                ["Poc_Header_Device"] = "设备：压装一线-01",
                ["Poc_Action_Language"] = "EN",
                ["Poc_Action_Dialog"] = "打开弹窗",
                ["Poc_Dialog_Title"] = "确认操作",
                ["Poc_Dialog_Message"] = "这是 Avalonia DialogHost 迁移验证弹窗。",
                ["Poc_Action_Close"] = "关闭",
                ["Poc_Tab_Monitor"] = "生产监控",
                ["Poc_Tab_IO"] = "IO 调试",
                ["Poc_Tool_Equipment"] = "设备面板",
                ["Poc_Tool_Log"] = "运行日志",
                ["Poc_Monitor_DayShift"] = "白班",
                ["Poc_Monitor_NightShift"] = "夜班",
                ["Poc_Monitor_Total"] = "总数",
                ["Poc_Monitor_Good"] = "良品",
                ["Poc_Monitor_Bad"] = "不良",
                ["Poc_Monitor_Yield"] = "良率",
                ["Poc_Monitor_GridTitle"] = "实时过程数据",
                ["Poc_IO_Title"] = "IO 读写矩阵",
                ["Poc_Column_Signal"] = "PLC 信号",
                ["Poc_Column_Address"] = "地址",
                ["Poc_Column_CurrentReply"] = "当前应答",
                ["Poc_Column_Write"] = "写入操作",
                ["Poc_Button_Write"] = "写入",
                ["Poc_Equipment_Title"] = "设备状态",
                ["Poc_Column_Status"] = "状态",
                ["Poc_Column_Name"] = "名称",
                ["Poc_Column_Connection"] = "连接",
                ["Poc_Column_Value"] = "当前值",
            },
            ["en-US"] = new Dictionary<string, string>
            {
                ["Poc_App_Title"] = "Edge Client Avalonia PoC",
                ["Poc_Header_Process"] = "Process Runtime Dashboard",
                ["Poc_Header_Device"] = "Device: Press Line 01",
                ["Poc_Action_Language"] = "中",
                ["Poc_Action_Dialog"] = "Open Dialog",
                ["Poc_Dialog_Title"] = "Confirm Operation",
                ["Poc_Dialog_Message"] = "This validates the Avalonia DialogHost migration path.",
                ["Poc_Action_Close"] = "Close",
                ["Poc_Tab_Monitor"] = "Monitor",
                ["Poc_Tab_IO"] = "IO Debug",
                ["Poc_Tool_Equipment"] = "Equipment",
                ["Poc_Tool_Log"] = "Runtime Log",
                ["Poc_Monitor_DayShift"] = "Day Shift",
                ["Poc_Monitor_NightShift"] = "Night Shift",
                ["Poc_Monitor_Total"] = "Total",
                ["Poc_Monitor_Good"] = "Good",
                ["Poc_Monitor_Bad"] = "Bad",
                ["Poc_Monitor_Yield"] = "Yield",
                ["Poc_Monitor_GridTitle"] = "Realtime Process Data",
                ["Poc_IO_Title"] = "IO Read/Write Matrix",
                ["Poc_Column_Signal"] = "PLC Signal",
                ["Poc_Column_Address"] = "Address",
                ["Poc_Column_CurrentReply"] = "Current Reply",
                ["Poc_Column_Write"] = "Write",
                ["Poc_Button_Write"] = "Write",
                ["Poc_Equipment_Title"] = "Equipment Status",
                ["Poc_Column_Status"] = "Status",
                ["Poc_Column_Name"] = "Name",
                ["Poc_Column_Connection"] = "Connection",
                ["Poc_Column_Value"] = "Current Value",
            }
        };

    public string CultureName { get; private set; } = "zh-CN";

    public string ToggleLabel => GetText("Poc_Action_Language");

    public string GetText(string key)
    {
        return Resources.TryGetValue(CultureName, out var current) && current.TryGetValue(key, out var value)
            ? value
            : key;
    }

    public void Apply(string cultureName)
    {
        var nextCulture = Resources.ContainsKey(cultureName) ? cultureName : "zh-CN";
        CultureName = nextCulture;

        if (Avalonia.Application.Current is not { } application)
        {
            return;
        }

        foreach (var pair in Resources[nextCulture])
        {
            application.Resources[pair.Key] = pair.Value;
        }
    }

    public void Toggle()
    {
        Apply(CultureName.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN");
    }

    public static string FindText(string key)
    {
        return Avalonia.Application.Current?.TryFindResource(key, out var value) == true
            ? value?.ToString() ?? key
            : key;
    }
}
