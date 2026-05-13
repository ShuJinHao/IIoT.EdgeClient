using CommunityToolkit.Mvvm.ComponentModel;
using IIoT.Edge.UI.Avalonia.Mvvm;
using IIoT.Edge.UI.Avalonia.Services;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Panels.Avalonia.ViewModels;

public sealed partial class EquipmentViewModel : AvaloniaViewModelBase
{
    private readonly IAvaloniaTimer _timer;
    private int _tick;

    public EquipmentViewModel(IAvaloniaTimerFactory timerFactory)
    {
        Items =
        [
            new EquipmentStatusRow("PLC 主站", "未启动", "等待运行链路"),
            new EquipmentStatusRow("扫码枪", "未启动", "等待运行链路"),
            new EquipmentStatusRow("MES 通道", "未启动", "等待运行链路")
        ];
        _timer = timerFactory.Create(TimeSpan.FromSeconds(5));
        _timer.Tick += (_, _) => RefreshHeartbeat();
        _timer.Start();
    }

    public override string ViewId => "Core.Equipment";

    public ObservableCollection<EquipmentStatusRow> Items { get; }

    private void RefreshHeartbeat()
    {
        _tick++;
        foreach (var item in Items)
        {
            item.LastValue = $"UI 心跳 {_tick}";
        }
    }
}

public sealed partial class EquipmentStatusRow : ObservableObject
{
    public EquipmentStatusRow(string name, string state, string lastValue)
    {
        Name = name;
        State = state;
        LastValue = lastValue;
    }

    public string Name { get; }

    public string State { get; }

    [ObservableProperty]
    private string lastValue;
}
