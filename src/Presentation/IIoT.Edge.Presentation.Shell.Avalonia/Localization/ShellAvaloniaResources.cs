using IIoT.Edge.UI.Avalonia.Localization;

namespace IIoT.Edge.Presentation.Shell.Avalonia.Localization;

public sealed class ShellAvaloniaZhCnResources : IAvaloniaResourceContributor
{
    public string CultureName => "zh-CN";

    public IReadOnlyDictionary<string, string> GetResources()
        => new Dictionary<string, string>
        {
            ["Shell_App_Title"] = "边缘客户端 Avalonia",
            ["Shell_Header_Process"] = "工序运行看板",
            ["Shell_Header_Device"] = "设备：未启动运行链路",
            ["Shell_Action_Language"] = "EN",
            ["Shell_Action_Login"] = "登录",
            ["Shell_Action_Logout"] = "注销",
            ["Shell_Action_Close"] = "关闭",
            ["Shell_Action_Clear"] = "清空",
            ["Shell_Dialog_Title"] = "确认操作",
            ["Shell_Dialog_Message"] = "这是 Avalonia DialogHost 迁移验证弹窗。",
            ["Shell_Login_Title"] = "用户登录",
            ["Shell_Login_CloudMode"] = "云端账号登录",
            ["Shell_Login_LocalMode"] = "本地紧急管理员",
            ["Shell_Login_EmployeeNo"] = "工号",
            ["Shell_Login_Password"] = "密码",
            ["Shell_Login_Submit"] = "登录",
            ["Shell_Login_SwitchToLocal"] = "切换本地管理员",
            ["Shell_Login_SwitchToCloud"] = "切换云端账号",
            ["Shell_Login_PasswordRequired"] = "密码不能为空。",
            ["Shell_Login_EmployeeNoRequired"] = "工号不能为空。",
            ["Shell_Login_DeviceNotReady"] = "设备云端身份尚未就绪。",
            ["Shell_Login_NotAuthenticated"] = "未登录",
            ["Shell_Footer_Unknown"] = "未知",
            ["Shell_Footer_Clock"] = "时间",
            ["Shell_Footer_Uptime"] = "运行",
            ["Shell_Footer_Status"] = "Avalonia 迁移骨架已启动，后台运行链路未启动。",
            ["Shell_EquipmentInfo"] = "设备信息",
            ["Shell_SystemLog"] = "系统日志",
            ["Shell_Column_Name"] = "名称",
            ["Shell_Column_Status"] = "状态",
            ["Shell_Column_Value"] = "数值",
            ["Shell_Column_Level"] = "级别",
            ["Shell_Column_Time"] = "时间",
            ["Shell_Column_Message"] = "消息"
        };
}

public sealed class ShellAvaloniaEnUsResources : IAvaloniaResourceContributor
{
    public string CultureName => "en-US";

    public IReadOnlyDictionary<string, string> GetResources()
        => new Dictionary<string, string>
        {
            ["Shell_App_Title"] = "IIoT Edge Client Avalonia",
            ["Shell_Header_Process"] = "Process Dashboard",
            ["Shell_Header_Device"] = "Device: runtime not started",
            ["Shell_Action_Language"] = "中",
            ["Shell_Action_Login"] = "Login",
            ["Shell_Action_Logout"] = "Logout",
            ["Shell_Action_Close"] = "Close",
            ["Shell_Action_Clear"] = "Clear",
            ["Shell_Dialog_Title"] = "Confirm",
            ["Shell_Dialog_Message"] = "This validates the Avalonia DialogHost migration path.",
            ["Shell_Login_Title"] = "User Login",
            ["Shell_Login_CloudMode"] = "Cloud Account",
            ["Shell_Login_LocalMode"] = "Local Emergency Admin",
            ["Shell_Login_EmployeeNo"] = "Employee No.",
            ["Shell_Login_Password"] = "Password",
            ["Shell_Login_Submit"] = "Login",
            ["Shell_Login_SwitchToLocal"] = "Use Local Admin",
            ["Shell_Login_SwitchToCloud"] = "Use Cloud Account",
            ["Shell_Login_PasswordRequired"] = "Password is required.",
            ["Shell_Login_EmployeeNoRequired"] = "Employee number is required.",
            ["Shell_Login_DeviceNotReady"] = "Cloud device identity is not ready.",
            ["Shell_Login_NotAuthenticated"] = "Not logged in",
            ["Shell_Footer_Unknown"] = "Unknown",
            ["Shell_Footer_Clock"] = "Time",
            ["Shell_Footer_Uptime"] = "Uptime",
            ["Shell_Footer_Status"] = "Avalonia migration shell is running; backend runtime is not started.",
            ["Shell_EquipmentInfo"] = "Equipment",
            ["Shell_SystemLog"] = "System Log",
            ["Shell_Column_Name"] = "Name",
            ["Shell_Column_Status"] = "Status",
            ["Shell_Column_Value"] = "Value",
            ["Shell_Column_Level"] = "Level",
            ["Shell_Column_Time"] = "Time",
            ["Shell_Column_Message"] = "Message"
        };
}
