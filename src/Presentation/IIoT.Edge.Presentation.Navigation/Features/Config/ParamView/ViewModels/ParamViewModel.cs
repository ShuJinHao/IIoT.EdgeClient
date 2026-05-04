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

/// <summary>
/// 参数配置页视图模型，只负责 MES、云端、业务三类插件参数。
/// </summary>
public class ParamViewModel : LocalizedCrudPageViewModelBase
{
    private readonly IParamViewCrudService _crudService;
    private readonly IClientPermissionService _permissionService;
    private readonly AsyncCommand _saveCommand;
    private int _selectedTabIndex;

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

    public ObservableCollection<ModuleParamGroupVm> MesParamGroups { get; } = new();
    public ObservableCollection<ModuleParamGroupVm> CloudParamGroups { get; } = new();
    public ObservableCollection<ModuleParamGroupVm> BusinessParamGroups { get; } = new();

    public ICommand SaveCommand { get; }

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

        _saveCommand = (AsyncCommand)CreateBusyCommand(SaveAsync, () => CanEdit);
        SaveCommand = _saveCommand;

        _permissionService.PermissionStateChanged += HandlePermissionStateChanged;
    }

    public override async Task OnActivatedAsync()
    {
        await ExecuteBusyAsync(InitAsync);
    }

    private async Task InitAsync()
    {
        var result = await _crudService.LoadAsync();

        ReplaceItems(MesParamGroups, result.MesParamGroups);
        ReplaceItems(CloudParamGroups, result.CloudParamGroups);
        ReplaceItems(BusinessParamGroups, result.BusinessParamGroups);
    }

    private async Task<CrudOperationResult> SaveAsync()
    {
        var moduleParams = MesParamGroups
            .Concat(CloudParamGroups)
            .Concat(BusinessParamGroups)
            .SelectMany(group => group.Params)
            .ToList();

        var saveResult = await _crudService.SaveAsync(moduleParams);
        if (!saveResult.IsSuccess)
        {
            return saveResult;
        }

        await RefreshAfterSaveAsync();

        return saveResult;
    }

    private async Task RefreshAfterSaveAsync()
    {
        var result = await _crudService.LoadAsync();
        ReplaceItems(MesParamGroups, result.MesParamGroups);
        ReplaceItems(CloudParamGroups, result.CloudParamGroups);
        ReplaceItems(BusinessParamGroups, result.BusinessParamGroups);
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
    }
}
