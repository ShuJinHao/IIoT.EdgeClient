using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

public sealed class IoInteractionRowModel : ObservableObject
{
    private bool _writeValueInitialized;
    private int _writeValue;
    private string _lastWriteValueText = "--";
    private string _lastWriteResultText = "尚未申请写入";

    public string BusinessGroup { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public string ListSeparator { get; init; } = "、";

    public ObservableCollection<IoSignalModel> PlcSignals { get; } = [];

    public ObservableCollection<IoSignalModel> HostSignals { get; } = [];

    public IAsyncRelayCommand? WriteCommand { get; set; }

    public bool CanWrite => HostSignals.Count > 0;

    public int WriteValue
    {
        get => _writeValue;
        set
        {
            if (SetProperty(ref _writeValue, value))
            {
                _writeValueInitialized = true;
            }
        }
    }

    public string LastWriteValueText
    {
        get => _lastWriteValueText;
        set => SetProperty(ref _lastWriteValueText, value);
    }

    public string LastWriteResultText
    {
        get => _lastWriteResultText;
        set => SetProperty(ref _lastWriteResultText, value);
    }

    public string PlcAddressSummary => FormatJoined(PlcSignals, static x => x.PlcAddress);

    public string PlcValueText => FormatJoined(PlcSignals, static x => x.DisplayValue);

    public string HostReplyAddressText => FormatJoined(HostSignals, static x => x.PlcAddress);

    public string HostReplyValueText => FormatJoined(HostSignals, static x => x.DisplayValue);

    public string PlcSignalToolTip => FormatSignalToolTip(PlcSignals);

    public string HostReplyToolTip => FormatSignalToolTip(HostSignals, includeValue: true);

    public void AddPlcSignal(IoSignalModel signal)
    {
        PlcSignals.Add(signal);
        NotifySignalLayoutChanged();
    }

    public void AddHostSignal(IoSignalModel signal)
    {
        HostSignals.Add(signal);
        NotifySignalLayoutChanged();
    }

    public void NotifyValuesChanged()
    {
        OnPropertyChanged(nameof(PlcValueText));
        OnPropertyChanged(nameof(HostReplyValueText));
        OnPropertyChanged(nameof(HostReplyToolTip));
    }

    public void InitializeWriteValueFromCurrentBuffer()
    {
        if (_writeValueInitialized || HostSignals.Count == 0)
        {
            return;
        }

        _writeValue = HostSignals[0].Value;
        _writeValueInitialized = true;
        OnPropertyChanged(nameof(WriteValue));
    }

    private string FormatJoined(IReadOnlyCollection<IoSignalModel> signals, Func<IoSignalModel, string> selector)
        => signals.Count == 0 ? "-" : string.Join(ListSeparator, signals.Select(selector));

    private string FormatSignalToolTip(IReadOnlyCollection<IoSignalModel> signals, bool includeValue = false)
        => signals.Count == 0
            ? "-"
            : string.Join(
                ListSeparator,
                signals.Select(signal =>
                {
                    var name = string.IsNullOrWhiteSpace(signal.SignalName) ? signal.SignalKey : signal.SignalName;
                    var value = includeValue ? $"{ListSeparator}当前值：{signal.DisplayValue}" : string.Empty;
                    return $"{name}{ListSeparator}信号键：{signal.SignalKey}{ListSeparator}地址：{signal.PlcAddress}{value}";
                }));

    private void NotifySignalLayoutChanged()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(PlcAddressSummary));
        OnPropertyChanged(nameof(PlcValueText));
        OnPropertyChanged(nameof(HostReplyAddressText));
        OnPropertyChanged(nameof(HostReplyValueText));
        OnPropertyChanged(nameof(PlcSignalToolTip));
        OnPropertyChanged(nameof(HostReplyToolTip));
    }
}
