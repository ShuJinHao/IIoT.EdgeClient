using CommunityToolkit.Mvvm.ComponentModel;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed partial class MonitorViewModel : NavigationPageViewModelBase
{
    private readonly IAvaloniaTimer _timer;
    private int _tick;

    public MonitorViewModel(
        IAvaloniaLanguageService languageService,
        IAvaloniaTimerFactory timerFactory,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        Devices =
        [
            new MonitorDeviceRow("压装一线-01", 128, 126, 2, "98.44%"),
            new MonitorDeviceRow("压装一线-02", 116, 115, 1, "99.14%")
        ];
        _timer = timerFactory.Create(TimeSpan.FromSeconds(2));
        _timer.Tick += (_, _) => RefreshSample();
        _timer.Start();
    }

    public ObservableCollection<MonitorDeviceRow> Devices { get; }

    private void RefreshSample()
    {
        _tick++;
        foreach (var row in Devices)
        {
            row.Status = $"UI 刷新 {_tick}";
        }
    }
}

public sealed partial class MonitorDeviceRow : ObservableObject
{
    public MonitorDeviceRow(string deviceName, int total, int ok, int ng, string yield)
    {
        DeviceName = deviceName;
        Total = total;
        Ok = ok;
        Ng = ng;
        Yield = yield;
        Status = "等待运行链路";
    }

    public string DeviceName { get; }
    public int Total { get; }
    public int Ok { get; }
    public int Ng { get; }
    public string Yield { get; }

    [ObservableProperty]
    private string status;
}
