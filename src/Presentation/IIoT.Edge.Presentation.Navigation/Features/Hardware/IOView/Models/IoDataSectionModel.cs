using System.Collections.ObjectModel;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public sealed class IoDataSectionModel : BaseNotifyPropertyChanged
{
    public string Category { get; init; } = IoMappingOptionCatalog.CategorySingleRead;

    public string BusinessGroup { get; init; } = string.Empty;

    public int SortOrder { get; set; }

    public string Title => IoMappingDisplay.BuildSectionTitle(Category, BusinessGroup);

    public ObservableCollection<IoSignalModel> Signals { get; } = [];

    public void NotifyLocalizationChanged()
        => OnPropertyChanged(nameof(Title));
}
