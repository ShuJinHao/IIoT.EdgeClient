using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public sealed class IoInteractionRowModel : BaseNotifyPropertyChanged
{
    private Func<string, string, string>? _textProvider;
    private bool _writeValueInitialized;
    private int _writeValue;

    public string BusinessGroup { get; init; } = "";

    public int SortOrder { get; set; }

    public ObservableCollection<IoSignalModel> PlcSignals { get; } = [];

    public ObservableCollection<IoSignalModel> HostSignals { get; } = [];

    public IoSignalModel? PlcSignal
    {
        get => PlcSignals.FirstOrDefault();
        set => ReplaceSignals(PlcSignals, value);
    }

    public IoSignalModel? HostSignal
    {
        get => HostSignals.FirstOrDefault();
        set => ReplaceSignals(HostSignals, value);
    }

    public ICommand? WriteCommand { get; set; }

    public bool CanWrite => HostSignal is not null;

    public int WriteValue
    {
        get => _writeValue;
        set
        {
            if (_writeValue == value && _writeValueInitialized)
            {
                return;
            }

            _writeValue = value;
            _writeValueInitialized = true;
            OnPropertyChanged();
        }
    }

    public string PlcSignalText => PlcSignal is null ? "-" : FormatSignal(PlcSignal);

    public string PlcAddressText => FormatJoined(PlcSignals, static x => x.PlcAddress);

    public string PlcValueText => FormatJoined(PlcSignals, static x => x.DisplayValue);

    public string PlcSignalSummary => PlcAddressSummary;

    public string PlcAddressSummary => FormatJoined(PlcSignals, static x => x.PlcAddress);

    public string PlcSignalToolTip => FormatSignalToolTip(PlcSignals);

    public string HostSignalText => HostSignal is null ? "-" : FormatSignal(HostSignal);

    public string HostAddressText => FormatJoined(HostSignals, static x => x.PlcAddress);

    public string HostValueText => FormatJoined(HostSignals, static x => x.DisplayValue);

    public string HostReplySummary => HostReplyAddressText;

    public string HostReplyAddressText => FormatJoined(HostSignals, static x => x.PlcAddress);

    public string HostReplyValueText => CurrentReplyValueText;

    public string HostReplyToolTip => FormatSignalToolTip(HostSignals, includeValue: true);

    public string CurrentReplyValueText => HostValueText;

    public void AddPlcSignal(IoSignalModel signal)
    {
        ApplyTextProvider(signal);
        PlcSignals.Add(signal);
        NotifySignalLayoutChanged();
    }

    public void AddHostSignal(IoSignalModel signal)
    {
        ApplyTextProvider(signal);
        HostSignals.Add(signal);
        NotifySignalLayoutChanged();
    }

    public void SetTextProvider(Func<string, string, string> textProvider)
    {
        _textProvider = textProvider;
        foreach (var signal in PlcSignals.Concat(HostSignals))
        {
            signal.SetTextProvider(textProvider);
        }

        NotifyLocalizationChanged();
    }

    public void NotifyValuesChanged()
    {
        OnPropertyChanged(nameof(PlcValueText));
        OnPropertyChanged(nameof(HostValueText));
        OnPropertyChanged(nameof(CurrentReplyValueText));
        OnPropertyChanged(nameof(HostReplyValueText));
        OnPropertyChanged(nameof(HostReplyToolTip));
    }

    public void NotifyLocalizationChanged()
    {
        OnPropertyChanged(nameof(PlcSignalText));
        OnPropertyChanged(nameof(PlcSignalToolTip));
        OnPropertyChanged(nameof(HostSignalText));
        OnPropertyChanged(nameof(HostReplyToolTip));
    }

    public void InitializeWriteValueFromCurrentBuffer()
    {
        if (_writeValueInitialized || HostSignal is null)
        {
            return;
        }

        _writeValue = HostSignal.Value;
        _writeValueInitialized = true;
        OnPropertyChanged(nameof(WriteValue));
    }

    private static string FormatSignal(IoSignalModel signal)
        => string.IsNullOrWhiteSpace(signal.SignalName)
            ? signal.SignalKey
            : signal.SignalName;

    private string FormatSignalToolTip(
        IReadOnlyCollection<IoSignalModel> signals,
        bool includeValue = false)
    {
        if (signals.Count == 0)
        {
            return "-";
        }

        var separator = GetText("Navigation_Io_TooltipSeparator", "；");
        return string.Join(separator, signals.Select(signal =>
        {
            var role = string.IsNullOrWhiteSpace(signal.SignalName)
                ? GetText("Navigation_Io_RoleUnset", "未设置")
                : signal.SignalName;
            var suffix = includeValue
                ? FormatText("Navigation_Io_TooltipCurrentValueFormat", "，当前值：{0}", signal.DisplayValue)
                : string.Empty;
            return FormatText(
                "Navigation_Io_TooltipSignalFormat",
                "{0}，信号键：{1}，地址：{2}{3}",
                role,
                signal.SignalKey,
                signal.PlcAddress,
                suffix);
        }));
    }

    private string FormatJoined(
        IReadOnlyCollection<IoSignalModel> signals,
        Func<IoSignalModel, string> selector)
        => signals.Count == 0
            ? "-"
            : string.Join(GetText("Navigation_ListSeparator", "、"), signals.Select(selector));

    private string GetText(string key, string fallback)
        => _textProvider?.Invoke(key, fallback) ?? fallback;

    private string FormatText(string key, string fallback, params object[] args)
        => string.Format(CultureInfo.CurrentUICulture, GetText(key, fallback), args);

    private void ApplyTextProvider(IoSignalModel signal)
    {
        if (_textProvider is not null)
        {
            signal.SetTextProvider(_textProvider);
        }
    }

    private void ReplaceSignals(ObservableCollection<IoSignalModel> signals, IoSignalModel? signal)
    {
        signals.Clear();
        if (signal is not null)
        {
            signals.Add(signal);
        }

        NotifySignalLayoutChanged();
    }

    private void NotifySignalLayoutChanged()
    {
        OnPropertyChanged(nameof(PlcSignal));
        OnPropertyChanged(nameof(HostSignal));
        OnPropertyChanged(nameof(PlcSignalText));
        OnPropertyChanged(nameof(PlcAddressText));
        OnPropertyChanged(nameof(PlcValueText));
        OnPropertyChanged(nameof(PlcSignalSummary));
        OnPropertyChanged(nameof(PlcAddressSummary));
        OnPropertyChanged(nameof(PlcSignalToolTip));
        OnPropertyChanged(nameof(HostSignalText));
        OnPropertyChanged(nameof(HostAddressText));
        OnPropertyChanged(nameof(HostValueText));
        OnPropertyChanged(nameof(HostReplySummary));
        OnPropertyChanged(nameof(HostReplyAddressText));
        OnPropertyChanged(nameof(HostReplyValueText));
        OnPropertyChanged(nameof(HostReplyToolTip));
        OnPropertyChanged(nameof(CurrentReplyValueText));
        OnPropertyChanged(nameof(CanWrite));
        (WriteCommand as BaseCommand)?.RaiseCanExecuteChanged();
    }
}
