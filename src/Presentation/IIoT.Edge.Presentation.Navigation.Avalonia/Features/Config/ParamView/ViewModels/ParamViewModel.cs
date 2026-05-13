using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Config.ParamView.Models;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Config.ParamView;

public sealed partial class ParamViewModel : NavigationPageViewModelBase
{
    private readonly IParamViewCrudService _crudService;
    private readonly IClientPermissionService _permissionService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaDialogService _dialogService;
    private readonly IAvaloniaDispatcherService _dispatcherService;
    private readonly AsyncRelayCommand _saveCommand;
    private bool _isSubscribed;

    public ParamViewModel(
        IParamViewCrudService crudService,
        IClientPermissionService permissionService,
        IAvaloniaLanguageService languageService,
        IAvaloniaDialogService dialogService,
        IAvaloniaDispatcherService dispatcherService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _crudService = crudService;
        _permissionService = permissionService;
        _languageService = languageService;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        _saveCommand = new AsyncRelayCommand(SaveAsync, () => CanEdit);
    }

    public ObservableCollection<ModuleParamGroupVm> MesParamGroups { get; } = [];

    public ObservableCollection<ModuleParamGroupVm> CloudParamGroups { get; } = [];

    public ObservableCollection<ModuleParamGroupVm> BusinessParamGroups { get; } = [];

    [ObservableProperty]
    private int selectedTabIndex;

    [ObservableProperty]
    private string feedbackMessage = string.Empty;

    public bool CanEdit => _permissionService.CanEditParams;

    public bool IsReadOnly => !CanEdit;

    public IAsyncRelayCommand SaveCommand => _saveCommand;

    public override async Task OnActivatedAsync()
    {
        if (!_isSubscribed)
        {
            _permissionService.PermissionStateChanged += HandlePermissionStateChanged;
            _isSubscribed = true;
        }

        await LoadAsync();
        RefreshPermissionState();
    }

    public override Task OnDeactivatedAsync()
    {
        if (_isSubscribed)
        {
            _permissionService.PermissionStateChanged -= HandlePermissionStateChanged;
            _isSubscribed = false;
        }

        return Task.CompletedTask;
    }

    private async Task LoadAsync()
    {
        var result = await _crudService.LoadAsync();
        Replace(MesParamGroups, result.MesParamGroups);
        Replace(CloudParamGroups, result.CloudParamGroups);
        Replace(BusinessParamGroups, result.BusinessParamGroups);
        ApplyParamLocalization();
    }

    private async Task SaveAsync()
    {
        if (!CanEdit)
        {
            var message = Text("Navigation_Param_NoPermission", "当前用户没有参数配置权限");
            FeedbackMessage = message;
            await _dialogService.ShowInfoAsync(ViewTitle, message);
            return;
        }

        var moduleParams = MesParamGroups
            .Concat(CloudParamGroups)
            .Concat(BusinessParamGroups)
            .SelectMany(static group => group.Params)
            .ToList();

        var result = await _crudService.SaveAsync(moduleParams);
        FeedbackMessage = GetOperationMessage(result);
        await _dialogService.ShowInfoAsync(ViewTitle, FeedbackMessage);
        if (result.IsSuccess)
        {
            await LoadAsync();
        }
    }

    private void HandlePermissionStateChanged()
        => _dispatcherService.Post(RefreshPermissionState);

    private void RefreshPermissionState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsReadOnly));
        _saveCommand.NotifyCanExecuteChanged();
    }

    private void ApplyParamLocalization()
    {
        foreach (var param in MesParamGroups
            .Concat(CloudParamGroups)
            .Concat(BusinessParamGroups)
            .SelectMany(static group => group.Params))
        {
            param.DisplayName = Text(param.DisplayNameResourceKey, param.DisplayNameFallback);
            param.Description = Text(param.DescriptionResourceKey, param.DescriptionFallback);
        }
    }

    private static void Replace(
        ObservableCollection<ModuleParamGroupVm> target,
        IEnumerable<ModuleParamGroupVm> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private string GetOperationMessage(CrudOperationResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            return result.Message;
        }

        return result.IsSuccess
            ? Text("Navigation_Param_SaveSuccess", "参数配置已保存")
            : Text("Navigation_Param_SaveFailed", "参数配置保存失败");
    }

    private string Text(string key, string fallback)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
