using System.ComponentModel;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public interface IHardwareConfigDeviceSelectionCoordinator
{
    void HandleSelectedNetworkDeviceChanged(HardwareConfigViewModel viewModel);

    void HandleSelectedNetworkDevicePropertyChanged(
        HardwareConfigViewModel viewModel,
        PropertyChangedEventArgs e);
}

public sealed class HardwareConfigDeviceSelectionCoordinator : IHardwareConfigDeviceSelectionCoordinator
{
    public void HandleSelectedNetworkDeviceChanged(HardwareConfigViewModel viewModel)
    {
        viewModel.SelectedIoMapping = null;
        viewModel.SetModuleTemplateAvailable(false);
        HardwareConfigViewModel.ReplaceCollection(viewModel.StandardIoSignals, Array.Empty<IoStandardSignalOptionVm>());
        HardwareConfigViewModel.ReplaceCollection(viewModel.StandardDataSignals, Array.Empty<IoStandardSignalOptionVm>());
        HardwareConfigViewModel.ReplaceCollection(viewModel.FilteredStandardDataSignals, Array.Empty<IoStandardSignalOptionVm>());
        HardwareConfigViewModel.ReplaceCollection(viewModel.StandardInteractionGroups, Array.Empty<IoStandardSignalGroupOptionVm>());
        viewModel.ModuleTemplateHint = "请选择 PLC 设备后导入插件标准点位。";
        viewModel.RefreshAddCommands();
    }

    public void HandleSelectedNetworkDevicePropertyChanged(
        HardwareConfigViewModel viewModel,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NetworkDeviceVm.DeviceType)
            or nameof(NetworkDeviceVm.Id))
        {
            _ = viewModel.RefreshModuleTemplateInfoAsync();
            viewModel.RefreshAddCommands();
        }
    }
}
