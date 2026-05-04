using System.Collections.ObjectModel;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public sealed class IoDataSectionModel : BaseNotifyPropertyChanged
{
    public string Category { get; init; } = "单点读数据";

    public string GroupName { get; init; } = string.Empty;

    public int SortOrder { get; set; }

    public string Title => IoMappingDisplay.BuildSectionTitle(Category, GroupName);

    public ObservableCollection<IoSignalModel> Signals { get; } = [];

    public void NotifyLocalizationChanged()
        => OnPropertyChanged(nameof(Title));
}
