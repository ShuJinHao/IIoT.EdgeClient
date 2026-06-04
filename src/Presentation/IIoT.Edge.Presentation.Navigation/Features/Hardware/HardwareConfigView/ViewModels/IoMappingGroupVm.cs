using System.Collections.ObjectModel;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public sealed class IoMappingGroupVm
{
    public IoMappingGroupVm(string title, IEnumerable<IoMappingVm> mappings)
    {
        Title = title;
        Mappings = new ObservableCollection<IoMappingVm>(mappings);
    }

    public string Title { get; }

    public ObservableCollection<IoMappingVm> Mappings { get; }
}
