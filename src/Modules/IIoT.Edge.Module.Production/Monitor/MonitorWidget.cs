// 路径：src/Modules/IIoT.Edge.Module.Production/Monitor/MonitorWidget.cs
using IIoT.Edge.Tasks.Context;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace IIoT.Edge.Module.Production.Monitor;

/// <summary>
/// 实时数据监控 ViewModel
/// 定时从 ProductionContextStore 读取数据刷新展示
/// </summary>
public class MonitorWidget : WidgetBase
{
    public override string WidgetId => "Core.Monitor";
    public override string WidgetName => "实时数据监控";

    private readonly ProductionContextStore _contextStore;
    private readonly DispatcherTimer _refreshTimer;

    /// <summary>
    /// 所有设备的监控数据
    /// </summary>
    public ObservableCollection<DeviceMonitorVm> Devices { get; } = new();

    public MonitorWidget(ProductionContextStore contextStore)
    {
        _contextStore = contextStore;

        // 500ms 定时刷新，避免高频事件卡UI
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
    }

    private void Refresh()
    {
        var contexts = _contextStore.GetAll();

        // 增删设备
        var currentIds = contexts.Select(c => c.DeviceId).ToHashSet();
        var removeList = Devices.Where(d => !currentIds.Contains(d.DeviceId)).ToList();
        foreach (var item in removeList)
            Devices.Remove(item);

        foreach (var ctx in contexts)
        {
            var vm = Devices.FirstOrDefault(d => d.DeviceId == ctx.DeviceId);
            if (vm is null)
            {
                vm = new DeviceMonitorVm { DeviceId = ctx.DeviceId };
                Devices.Add(vm);
            }

            // 更新设备信息
            vm.DeviceName = ctx.DeviceName;

            // 更新状态机步骤
            vm.StepStates.Clear();
            foreach (var kv in ctx.StepStates)
                vm.StepStates.Add(new KeyValueVm(kv.Key, kv.Value.ToString()));

            // 更新设备级数据
            vm.DeviceData.Clear();
            foreach (var kv in ctx.DeviceBag.OrderBy(x => x.Key))
                vm.DeviceData.Add(new KeyValueVm(kv.Key, FormatValue(kv.Value)));

            // 更新在制电芯
            vm.CellCount = ctx.CellBags.Count;
            vm.Cells.Clear();
            foreach (var cell in ctx.CellBags)
            {
                var cellVm = new CellMonitorVm { Barcode = cell.Key };
                foreach (var kv in cell.Value.OrderBy(x => x.Key))
                    cellVm.Data.Add(new KeyValueVm(kv.Key, FormatValue(kv.Value)));
                vm.Cells.Add(cellVm);
            }
        }
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            DateTime dt => dt.ToString("HH:mm:ss.fff"),
            bool b => b ? "OK" : "NG",
            double d => d.ToString("F3"),
            _ => value.ToString() ?? ""
        };
    }
}

/// <summary>
/// 单台设备的监控展示模型
/// </summary>
public class DeviceMonitorVm : IIoT.Edge.Common.Mvvm.BaseNotifyPropertyChanged
{
    public int DeviceId { get; set; }

    private string _deviceName = "";
    public string DeviceName
    {
        get => _deviceName;
        set { _deviceName = value; OnPropertyChanged(); }
    }

    private int _cellCount;
    public int CellCount
    {
        get => _cellCount;
        set { _cellCount = value; OnPropertyChanged(); }
    }

    public ObservableCollection<KeyValueVm> StepStates { get; } = new();
    public ObservableCollection<KeyValueVm> DeviceData { get; } = new();
    public ObservableCollection<CellMonitorVm> Cells { get; } = new();
}

/// <summary>
/// 单个电芯的监控展示模型
/// </summary>
public class CellMonitorVm
{
    public string Barcode { get; set; } = "";
    public ObservableCollection<KeyValueVm> Data { get; } = new();
}

/// <summary>
/// 键值对展示模型
/// </summary>
public class KeyValueVm
{
    public string Key { get; set; }
    public string Value { get; set; }

    public KeyValueVm(string key, string value)
    {
        Key = key;
        Value = value;
    }
}