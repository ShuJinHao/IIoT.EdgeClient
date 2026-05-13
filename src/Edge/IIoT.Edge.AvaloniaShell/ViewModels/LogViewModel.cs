using System.Collections.ObjectModel;

namespace IIoT.Edge.AvaloniaShell.ViewModels;

public sealed class LogViewModel
{
    public ObservableCollection<string> Entries { get; } =
    [
        "08:31:12 PLC 块读取完成，站点 A-001",
        "08:31:18 Cloud 上传门闩打开",
        "08:31:26 MES 心跳检测成功",
        "08:31:40 本地上下文 step 已持久化"
    ];
}
