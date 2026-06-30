using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Presentation.Navigation.Features.Hardware;
using IIoT.Edge.UI.Shared.Localization;

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
    private readonly IHardwareConfigEditModelMapper _modelMapper;
    private readonly IAppLanguageService _languageService;

    public HardwareConfigLoadSaveCoordinator(
        IHardwareConfigCrudService crudService,
        IHardwareConfigValidationPresenter validationPresenter,
        IHardwareConfigEditSession editSession,
        IHardwareConfigEditModelMapper modelMapper,
        IAppLanguageService languageService)
    {
        _crudService = crudService;
        _validationPresenter = validationPresenter;
        _editSession = editSession;
        _modelMapper = modelMapper;
        _languageService = languageService;
    }

    public async Task LoadAllAsync(HardwareConfigViewModel viewModel)
    {
        var result = await _crudService.LoadAsync();

        HardwareConfigViewModel.ReplaceCollection(
            viewModel.NetworkDevices,
            result.NetworkDevices.Select(_modelMapper.ToNetworkDeviceVm));
        HardwareConfigViewModel.ReplaceCollection(
            viewModel.SerialDevices,
            result.SerialDevices.Select(_modelMapper.ToSerialDeviceVm));
        viewModel.RefreshIoMappingNetworkDevices();

        if (viewModel.IoMappingNetworkDevices.Count > 0)
        {
            viewModel.ApplyIoMappingSelectionFromSharedSelection();
        }
        else
        {
            viewModel.SelectedNetworkDevice = null;
            viewModel.SetModuleTemplateAvailable(false);
            HardwareConfigViewModel.ReplaceCollection(viewModel.IoMappings, Array.Empty<IoMappingVm>());
            viewModel.RefreshIoMappingGroups();
        }
    }

    public async Task RefreshSelectedNetworkDeviceAsync(HardwareConfigViewModel viewModel)
    {
        await LoadIoMappingsAsync(viewModel);
        await RefreshModuleTemplateInfoAsync(viewModel);
    }

    public async Task RefreshModuleTemplateInfoAsync(HardwareConfigViewModel viewModel)
    {
        var selectedDevice = viewModel.SelectedNetworkDevice is null
            ? null
            : _modelMapper.ToNetworkDeviceDto(viewModel.SelectedNetworkDevice);
        var result = await _crudService.GetModuleTemplateInfoAsync(selectedDevice);
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
        if (!await ConfirmResetModuleTemplateAsync())
        {
            return CrudOperationResult.Success("已取消重置标准点位。");
        }

        var selectedDevice = viewModel.SelectedNetworkDevice is null
            ? null
            : _modelMapper.ToNetworkDeviceDto(viewModel.SelectedNetworkDevice);
        var result = await _crudService.ApplyModuleTemplateAsync(selectedDevice);
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
        var selectedNetworkDeviceId = viewModel.SelectedNetworkDevice?.Id ?? 0;

        var saveResult = await _crudService.SaveAsync(
            viewModel.NetworkDevices.Select(_modelMapper.ToNetworkDeviceDto).ToArray(),
            viewModel.SerialDevices.Select(_modelMapper.ToSerialDeviceDto).ToArray(),
            selectedNetworkDeviceId,
            mappingsToSave.Select(mapping => _modelMapper.ToIoMappingDto(mapping, selectedNetworkDeviceId)).ToArray());

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
            viewModel.SelectedInteractionPair = null;
            viewModel.RefreshIoMappingGroups();
            return;
        }

        var result = await _crudService.LoadIoMappingsAsync(viewModel.SelectedNetworkDevice.Id);
        HardwareConfigViewModel.ReplaceCollection(
            viewModel.IoMappings,
            result.Items.Select(_modelMapper.ToIoMappingVm));
        viewModel.SelectedIoMapping = null;
        viewModel.SelectedInteractionPair = null;
        viewModel.RefreshIoMappingGroups();
    }

    private Task<bool> ConfirmResetModuleTemplateAsync()
    {
        var title = _languageService.GetString(
            "Navigation_Hardware_ResetStandardIoConfirmTitle",
            "重置标准点位");
        var message = _languageService.GetString(
            "Navigation_Hardware_ResetStandardIoConfirmMessage",
            "重置标准点位会清空当前 PLC 已有 IO 映射，并按插件标准模板重新生成。是否继续？");

        return ConfirmAsync(title, message);
    }

    private static async Task<bool> ConfirmAsync(string title, string message)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            var result = new TaskCompletionSource<bool>();
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    result.SetResult(await ConfirmOnUiThreadAsync(title, message));
                }
                catch (Exception ex)
                {
                    result.SetException(ex);
                }
            });
            return await result.Task;
        }

        return await ConfirmOnUiThreadAsync(title, message);
    }

    private static async Task<bool> ConfirmOnUiThreadAsync(string title, string message)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return false;
        }

        var owner = lifetime.Windows.FirstOrDefault(static window => window.IsActive)
                    ?? lifetime.MainWindow;
        if (owner is null)
        {
            return false;
        }

        var dialog = new HardwareConfirmationDialog(title, message);
        return await dialog.ShowDialog<bool>(owner);
    }
}
