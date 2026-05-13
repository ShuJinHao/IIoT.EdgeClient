using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

public sealed class IoSignalModel : ObservableObject
{
    private int _value;
    private string _displayValue = "0";
    private string _previewValue = "0";

    public string SignalKey { get; init; } = string.Empty;

    public string PlcAddress { get; init; } = string.Empty;

    public string Direction { get; init; } = "Read";

    public string DirectionText { get; init; } = string.Empty;

    public string DataType { get; init; } = "Int16";

    public string SignalName { get; init; } = string.Empty;

    public string MatrixColumnTitle => string.IsNullOrWhiteSpace(SignalName) ? SignalKey : SignalName;

    public string? Remark { get; init; }

    public int StartIndex { get; init; }

    public int AddressCount { get; init; } = 1;

    public int SortOrder { get; init; }

    public string LengthText => AddressCount <= 1 ? "1" : AddressCount.ToString();

    public bool IsContinuous => AddressCount > 1;

    public bool HasExpandedValues => ExpandedValues.Count > 0;

    public ObservableCollection<IoSignalValueModel> ExpandedValues { get; } = [];

    public int Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public string DisplayValue
    {
        get => _displayValue;
        set => SetProperty(ref _displayValue, value);
    }

    public string PreviewValue
    {
        get => _previewValue;
        set => SetProperty(ref _previewValue, value);
    }

    public void SetValue(int value)
    {
        Value = value;
        DisplayValue = value.ToString();
        PreviewValue = DisplayValue;
    }
}

public sealed record IoSignalValueModel(int Index, string Value);
