using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Config.ParamView.Models;
using IIoT.Edge.Presentation.Navigation.Common.Crud;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;

public class ParamViewModel : CrudPageViewModelBase
{
    private readonly IParamViewCrudService _crudService;
    private readonly IEditorValidator<GeneralParamVm> _generalParamValidator = new GeneralParamValidator();
    private readonly IEditorValidator<DeviceParamVm> _deviceParamValidator = new DeviceParamValidator();
    private readonly string _viewId;
    private readonly string _viewTitle;
    private int _selectedTabIndex;
    private DeviceParamGroupVm? _selectedGroup;

    public override string ViewId => _viewId;
    public override string ViewTitle => _viewTitle;

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

    public ParamViewModel(IParamViewCrudService crudService)
        : this(crudService, "Config.ParamView", "Parameter Config")
    {
    }

    protected ParamViewModel(
        IParamViewCrudService crudService,
        string viewId,
        string viewTitle)
    {
        _crudService = crudService;
        _viewId = viewId;
        _viewTitle = viewTitle;

        SaveCommand = CreateBusyCommand(SaveAsync);
        AddGeneralParamCommand = CreateAddCommand(GeneralParams, () => new GeneralParamVm());
        DeleteGeneralParamCommand = CreateDeleteCommand(GeneralParams);
        AddDeviceParamCommand = CreateScopedAddCommand(() => SelectedGroup?.Params, () => new DeviceParamVm());
        DeleteDeviceParamCommand = CreateScopedDeleteCommand(() => SelectedGroup?.Params);
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
            return CrudOperationResult.Failure("Please select a device group first.");
        }

        var issues = new List<ValidationIssue>();
        issues.AddRange(await ValidateAsync(GeneralParams, _generalParamValidator));
        issues.AddRange(await ValidateAsync(SelectedGroup.Params, _deviceParamValidator));

        var validationResult = CreateValidationResult(issues);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        await _crudService.SaveAsync(GeneralParams, SelectedGroup.DeviceId, SelectedGroup.Params);

        await LoadDeviceParamsAsync(SelectedGroup);
        var result = await _crudService.LoadAsync();
        ReplaceItems(GeneralParams, result.GeneralParams);

        return CrudOperationResult.Success("Parameter configuration saved.");
    }
}
