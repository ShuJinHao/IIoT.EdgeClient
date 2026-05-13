using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public interface IHardwareConfigLoadSaveCoordinator
{
    Task LoadAllAsync(HardwareConfigViewModel viewModel);

    Task RefreshSelectedNetworkDeviceAsync(HardwareConfigViewModel viewModel);

    Task RefreshModuleTemplateInfoAsync(HardwareConfigViewModel viewModel);

    Task<CrudOperationResult> ApplyModuleTemplateAsync(HardwareConfigViewModel viewModel);

    Task<CrudOperationResult> SaveAsync(HardwareConfigViewModel viewModel);
}

public sealed class HardwareConfigLoadSaveCoordinator : IHardwareConfigLoadSaveCoordinator
{
    private readonly IHardwareConfigCrudService _crudService;
    private readonly IHardwareConfigValidationPresenter _validationPresenter;
    private readonly IHardwareConfigEditSession _editSession;

    public HardwareConfigLoadSaveCoordinator(
        IHardwareConfigCrudService crudService,
        IHardwareConfigValidationPresenter validationPresenter,
        IHardwareConfigEditSession editSession)
    {
        _crudService = crudService;
        _validationPresenter = validationPresenter;
        _editSession = editSession;
    }

    public async Task LoadAllAsync(HardwareConfigViewModel viewModel)
    {
        var result = await _crudService.LoadAsync();

        HardwareConfigViewModel.ReplaceCollection(viewModel.NetworkDevices, result.NetworkDevices);
        HardwareConfigViewModel.ReplaceCollection(viewModel.SerialDevices, result.SerialDevices);

        if (viewModel.NetworkDevices.Count > 0)
        {
            viewModel.SelectedNetworkDevice = viewModel.NetworkDevices[0];
        }
        else
        {
            viewModel.SetModuleTemplateAvailable(false);
            HardwareConfigViewModel.ReplaceCollection(viewModel.IoMappings, Array.Empty<IoMappingVm>());
        }
    }

    public async Task RefreshSelectedNetworkDeviceAsync(HardwareConfigViewModel viewModel)
    {
        await LoadIoMappingsAsync(viewModel);
        await RefreshModuleTemplateInfoAsync(viewModel);
    }

    public async Task RefreshModuleTemplateInfoAsync(HardwareConfigViewModel viewModel)
    {
        var result = await _crudService.GetModuleTemplateInfoAsync(viewModel.SelectedNetworkDevice);
        var defaultSignals = result.DefaultSignals.ToArray();
        var candidateSignals = result.CandidateSignals.Count == 0
            ? defaultSignals
            : result.CandidateSignals.ToArray();

        HardwareConfigViewModel.ReplaceCollection(
            viewModel.StandardIoSignals,
            defaultSignals.Select(static x => new IoStandardSignalOptionVm(x)));
        HardwareConfigViewModel.ReplaceCollection(
            viewModel.StandardDataSignals,
            candidateSignals
                .Where(static x => IoMappingOptionCatalog.IsDataPointCategory(
                    IoMappingOptionCatalog.NormalizeCategory(x.Category, x.AddressCount)))
                .Select(static x => new IoStandardSignalOptionVm(x)));
        _editSession.RefreshFilteredStandardDataSignals(viewModel);
        HardwareConfigViewModel.ReplaceCollection(
            viewModel.StandardInteractionGroups,
            candidateSignals
                .Where(static x => string.Equals(
                    IoMappingOptionCatalog.NormalizeCategory(x.Category, x.AddressCount),
                    IoMappingOptionCatalog.CategoryInteraction,
                    StringComparison.OrdinalIgnoreCase))
                .GroupBy(static x => x.SignalKey.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(static x => new IoStandardSignalGroupOptionVm(
                    x.FirstOrDefault()?.BusinessGroup ?? x.Key,
                    x.ToArray()))
                .OrderBy(static x => x.BusinessGroup, StringComparer.OrdinalIgnoreCase));
        viewModel.ModuleTemplateHint = result.Message;
        viewModel.SetModuleTemplateAvailable(result.IsAvailable);
    }

    public async Task<CrudOperationResult> ApplyModuleTemplateAsync(HardwareConfigViewModel viewModel)
    {
        if (!ConfirmResetModuleTemplate())
        {
            return CrudOperationResult.Success("已取消重置标准点位。");
        }

        var result = await _crudService.ApplyModuleTemplateAsync(viewModel.SelectedNetworkDevice);
        if (result.IsSuccess)
        {
            await LoadIoMappingsAsync(viewModel);
            await RefreshModuleTemplateInfoAsync(viewModel);
        }

        return result;
    }

    public async Task<CrudOperationResult> SaveAsync(HardwareConfigViewModel viewModel)
    {
        var issues = await _validationPresenter.ValidateSaveAsync(viewModel);
        var validationResult = _validationPresenter.CreateValidationResult(viewModel, issues);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        var mappingsToSave = _editSession.BuildMappingsToSave(viewModel.IoMappings);

        var saveResult = await _crudService.SaveAsync(
            viewModel.NetworkDevices,
            viewModel.SerialDevices,
            viewModel.SelectedNetworkDevice?.Id ?? 0,
            mappingsToSave);

        if (saveResult.IsSuccess
            || saveResult.Message.StartsWith("配置已保存", StringComparison.Ordinal))
        {
            await LoadAllAsync(viewModel);
        }

        return saveResult;
    }

    private async Task LoadIoMappingsAsync(HardwareConfigViewModel viewModel)
    {
        if (viewModel.SelectedNetworkDevice is null || viewModel.SelectedNetworkDevice.Id <= 0)
        {
            HardwareConfigViewModel.ReplaceCollection(viewModel.IoMappings, Array.Empty<IoMappingVm>());
            viewModel.SelectedIoMapping = null;
            viewModel.IoMappingsView.Refresh();
            return;
        }

        var result = await _crudService.LoadIoMappingsAsync(viewModel.SelectedNetworkDevice.Id);
        HardwareConfigViewModel.ReplaceCollection(viewModel.IoMappings, result.Items);
        viewModel.SelectedIoMapping = null;
        viewModel.IoMappingsView.Refresh();
    }

    private static bool ConfirmResetModuleTemplate()
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return true;
        }

        var result = System.Windows.MessageBox.Show(
            "重置标准点位会清空当前 PLC 已有 IO 映射，并按插件标准模板重新生成。是否继续？",
            "重置标准点位",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning);

        return result == System.Windows.MessageBoxResult.OK;
    }
}
