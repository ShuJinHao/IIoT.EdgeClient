using System.Collections.ObjectModel;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public sealed class IoDataSectionModel : BaseNotifyPropertyChanged
{
    public string Category { get; init; } = IoMappingOptionCatalog.CategorySingleRead;

    public string BusinessGroup { get; init; } = string.Empty;

    public int SortOrder { get; set; }

    public bool CanManualRead => IoMappingOptionCatalog.IsReadDataCategory(Category);

    public string Title => IoMappingDisplay.BuildSectionTitle(Category, BusinessGroup);

    public string DisplayTitle => $"{Title} ({Signals.Count})";

    public ObservableCollection<IoSignalModel> Signals { get; } = [];

    public void NotifyLocalizationChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DisplayTitle));
    }
}
