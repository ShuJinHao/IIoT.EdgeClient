using System.Collections.ObjectModel;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public class IoSignalModel : BaseNotifyPropertyChanged
{
    public string SignalKey { get; set; } = "";
    public string PlcAddress { get; set; } = "";
    public string Direction { get; set; } = "Read";
    public string DirectionText => Direction == "Write"
        ? GetText("Navigation_Io_Direction_HostToPlc", "上位机到 PLC")
        : GetText("Navigation_Io_Direction_PlcToHost", "PLC 到上位机");
    public string DataType { get; set; } = "Int16";
    public string SignalName { get; set; } = "";
    public string MatrixColumnTitle => string.IsNullOrWhiteSpace(SignalName) ? SignalKey : SignalName;
    public string? Remark { get; set; }
    public int StartIndex { get; set; }
    public int AddressCount { get; set; } = 1;
    public int SortOrder { get; set; }

    public string LengthText => AddressCount <= 1 ? "1" : AddressCount.ToString();

    public bool IsContinuous => AddressCount > 1;

    public bool HasExpandedValues => ExpandedValues.Count > 0;

    public ObservableCollection<IoSignalValueModel> ExpandedValues { get; } = [];

    public void NotifyLocalizationChanged()
        => OnPropertyChanged(nameof(DirectionText));

    private int _value;
    public int Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            OnPropertyChanged();
        }
    }

    private string _displayValue = "0";
    public string DisplayValue
    {
        get => _displayValue;
        set
        {
            if (_displayValue == value)
            {
                return;
            }

            _displayValue = value;
            OnPropertyChanged();
        }
    }

    private string _previewValue = "0";
    public string PreviewValue
    {
        get => _previewValue;
        set
        {
            if (_previewValue == value)
            {
                return;
            }

            _previewValue = value;
            OnPropertyChanged();
        }
    }

    private static string GetText(string key, string fallback)
        => System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
}

public sealed class IoSignalValueModel
{
    public int Index { get; init; }

    public string Value { get; init; } = "";
}
