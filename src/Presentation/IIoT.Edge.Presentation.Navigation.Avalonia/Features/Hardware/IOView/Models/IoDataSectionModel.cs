using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

public sealed class IoDataSectionModel : ObservableObject
{
    public string Category { get; init; } = string.Empty;

    public string BusinessGroup { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    public bool CanManualRead { get; init; } = true;

    public string Title { get; init; } = string.Empty;

    public ObservableCollection<IoSignalModel> Signals { get; } = [];
}
