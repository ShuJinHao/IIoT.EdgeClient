using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IIoT.Edge.AvaloniaShell.ViewModels;

public sealed partial class IoViewModel : ObservableObject
{
    public IoViewModel()
    {
        Rows =
        [
            new IoSignalRow("入站扫码完成", "M100", "True", "Running"),
            new IoSignalRow("压装允许", "M110", "False", "Warning"),
            new IoSignalRow("出站复位", "M120", "True", "Connected"),
            new IoSignalRow("急停状态", "M130", "False", "Error")
        ];
    }

    public ObservableCollection<IoSignalRow> Rows { get; }

    [RelayCommand]
    private void Write(IoSignalRow row)
    {
        row.CurrentReply = row.CurrentReply == "True" ? "False" : "True";
        row.StatusKind = row.CurrentReply == "True" ? "Connected" : "Warning";
    }
}

public sealed partial class IoSignalRow : ObservableObject
{
    public IoSignalRow(string signal, string address, string currentReply, string statusKind)
    {
        Signal = signal;
        Address = address;
        CurrentReply = currentReply;
        StatusKind = statusKind;
    }

    public string Signal { get; }

    public string Address { get; }

    [ObservableProperty]
    private string currentReply;

    [ObservableProperty]
    private string statusKind;
}
