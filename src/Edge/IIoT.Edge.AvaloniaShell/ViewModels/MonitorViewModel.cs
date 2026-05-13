using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace IIoT.Edge.AvaloniaShell.ViewModels;

public sealed partial class MonitorViewModel : ObservableObject
{
    public MonitorViewModel()
    {
        Rows =
        [
            new("A-001", "扫码完成", "设备应答正常", "Cloud 待补传 0 条", "MES 心跳正常", 18),
            new("A-002", "压装中", "压力 1.28 MPa", "Cloud 上传打开", "MES 待补传 1 条", 22),
            new("A-003", "等待出站", "PLC M120 已复位", "Cloud 上传打开", "MES 心跳正常", 20)
        ];
    }

    public ObservableCollection<MonitorRow> Rows { get; }

    [ObservableProperty]
    private int dayTotal = 428;

    [ObservableProperty]
    private int dayGood = 421;

    [ObservableProperty]
    private int dayBad = 7;

    [ObservableProperty]
    private string dayYield = "98.36%";

    [ObservableProperty]
    private int nightTotal = 316;

    [ObservableProperty]
    private int nightGood = 312;

    [ObservableProperty]
    private int nightBad = 4;

    [ObservableProperty]
    private string nightYield = "98.73%";
}

public sealed record MonitorRow(
    string Station,
    string StateMachine,
    string DeviceData,
    string CloudSync,
    string MesSync,
    int WipCount);
