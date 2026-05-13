using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace IIoT.Edge.AvaloniaShell.ViewModels;

public sealed partial class EquipmentViewModel : ObservableObject
{
    public EquipmentViewModel()
    {
        Rows =
        [
            new("PLC 主站", "Connected", "在线", "192.168.10.21"),
            new("压装伺服", "Running", "运行", "1.28 MPa"),
            new("扫码器", "Connected", "在线", "COM3"),
            new("MES 通道", "Warning", "待补传", "1 条")
        ];
    }

    public ObservableCollection<EquipmentRow> Rows { get; }
}

public sealed record EquipmentRow(
    string Name,
    string StatusKind,
    string Connection,
    string CurrentValue);
