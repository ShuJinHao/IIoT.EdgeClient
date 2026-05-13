using IIoT.Edge.UI.Avalonia.Localization;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed class PlcTaskBindingViewModel : NavigationPageViewModelBase
{
    public PlcTaskBindingViewModel(
        IAvaloniaLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        Bindings =
        [
            new PlcTaskBindingRow("Heartbeat", "PLC 心跳", "等待运行链路"),
            new PlcTaskBindingRow("Realtime", "实时采集", "等待运行链路"),
            new PlcTaskBindingRow("Inbound", "进站", "等待运行链路"),
            new PlcTaskBindingRow("Outbound", "出站", "等待运行链路")
        ];
    }

    public ObservableCollection<PlcTaskBindingRow> Bindings { get; }
}

public sealed record PlcTaskBindingRow(string TaskKey, string Signal, string Binding);
