using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Config.ParamView.Models;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;

public class ParamViewModel : LocalizedCrudPageViewModelBase
{
    private readonly IParamViewCrudService _crudService;
    private readonly IClientPermissionService _permissionService;
    private readonly IEditorValidator<GeneralParamVm> _generalParamValidator;
    private readonly IEditorValidator<DeviceParamVm> _deviceParamValidator;
    private readonly AsyncCommand _saveCommand;
    private readonly BaseCommand _addGeneralParamCommand;
    private readonly BaseCommand _deleteGeneralParamCommand;
    private readonly BaseCommand _addDeviceParamCommand;
    private readonly BaseCommand _deleteDeviceParamCommand;
    private int _selectedTabIndex;
    private DeviceParamGroupVm? _selectedGroup;

    public bool CanEdit => _permissionService.CanEditParams;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            _selectedTabIndex = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<GeneralParamVm> GeneralParams { get; } = new();
    public ObservableCollection<DeviceParamGroupVm> DeviceParamGroups { get; } = new();

    public DeviceParamGroupVm? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            _selectedGroup = value;
            OnPropertyChanged();
            if (value is not null)
            {
                _ = LoadDeviceParamsAsync(value);
            }
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand AddGeneralParamCommand { get; }
    public ICommand DeleteGeneralParamCommand { get; }
    public ICommand AddDeviceParamCommand { get; }
    public ICommand DeleteDeviceParamCommand { get; }

    public ParamViewModel(
        IParamViewCrudService crudService,
        IClientPermissionService permissionService,
        IAppLanguageService languageService)
        : this(
            crudService,
            permissionService,
            languageService,
            "Config.ParamView",
            "Navigation_Title_ParamConfig",
            "参数配置")
    {
    }

    public ParamViewModel(
        IParamViewCrudService crudService,
        IClientPermissionService permissionService,
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _crudService = crudService;
        _permissionService = permissionService;
        _generalParamValidator = new GeneralParamValidator(GetText);
        _deviceParamValidator = new DeviceParamValidator(GetText);

        _saveCommand = (AsyncCommand)CreateBusyCommand(SaveAsync, () => CanEdit);
        _addGeneralParamCommand = (BaseCommand)CreateAddCommand(GeneralParams, () => new GeneralParamVm(), () => CanEdit);
        _deleteGeneralParamCommand = (BaseCommand)CreateDeleteCommand(GeneralParams, () => CanEdit);
        _addDeviceParamCommand = (BaseCommand)CreateScopedAddCommand(
            () => SelectedGroup?.Params,
            () => new DeviceParamVm(),
            () => CanEdit);
        _deleteDeviceParamCommand = (BaseCommand)CreateScopedDeleteCommand(() => SelectedGroup?.Params, () => CanEdit);

        SaveCommand = _saveCommand;
        AddGeneralParamCommand = _addGeneralParamCommand;
        DeleteGeneralParamCommand = _deleteGeneralParamCommand;
        AddDeviceParamCommand = _addDeviceParamCommand;
        DeleteDeviceParamCommand = _deleteDeviceParamCommand;

        _permissionService.PermissionStateChanged += HandlePermissionStateChanged;
    }

    public override async Task OnActivatedAsync()
    {
        await ExecuteBusyAsync(InitAsync);
    }

    private async Task InitAsync()
    {
        var result = await _crudService.LoadAsync();

        ReplaceItems(GeneralParams, result.GeneralParams);

        DeviceParamGroups.Clear();
        foreach (var header in result.DeviceGroups)
        {
            DeviceParamGroups.Add(new DeviceParamGroupVm
            {
                DeviceId = header.DeviceId,
                DeviceName = header.DeviceName
            });
        }

        if (DeviceParamGroups.Count > 0)
        {
            SelectedGroup = DeviceParamGroups[0];
        }
    }

    private async Task LoadDeviceParamsAsync(DeviceParamGroupVm group)
    {
        var parameters = await _crudService.LoadDeviceParamsAsync(group.DeviceId);
        ReplaceItems(group.Params, parameters);
    }

    private async Task<CrudOperationResult> SaveAsync()
    {
        if (SelectedGroup is null)
        {
            return CrudOperationResult.Failure(GetText(
                "Navigation_Param_SelectDeviceGroupFirst",
                "请先选择设备参数组。"));
        }

        var issues = new List<ValidationIssue>();
        issues.AddRange(await ValidateAsync(GeneralParams, _generalParamValidator));
        issues.AddRange(await ValidateAsync(SelectedGroup.Params, _deviceParamValidator));

        var validationResult = CreateValidationResult(issues);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        var saveResult = await _crudService.SaveAsync(GeneralParams, SelectedGroup.DeviceId, SelectedGroup.Params);
        if (!saveResult.IsSuccess)
        {
            return saveResult;
        }

        await RefreshAfterSaveAsync(SelectedGroup);

        return saveResult;
    }

    private async Task RefreshAfterSaveAsync(DeviceParamGroupVm selectedGroup)
    {
        var result = await _crudService.LoadAsync();
        ReplaceItems(GeneralParams, result.GeneralParams);
        await LoadDeviceParamsAsync(selectedGroup);
    }

    private void HandlePermissionStateChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RefreshPermissionState();
            return;
        }

        dispatcher.Invoke(RefreshPermissionState);
    }

    private void RefreshPermissionState()
    {
        OnPropertyChanged(nameof(CanEdit));
        _saveCommand.RaiseCanExecuteChanged();
        _addGeneralParamCommand.RaiseCanExecuteChanged();
        _deleteGeneralParamCommand.RaiseCanExecuteChanged();
        _addDeviceParamCommand.RaiseCanExecuteChanged();
        _deleteDeviceParamCommand.RaiseCanExecuteChanged();
    }
}
