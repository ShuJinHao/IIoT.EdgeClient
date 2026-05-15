using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.SharedKernel.DataPipeline.Recipe;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Formula.RecipeView;

public sealed partial class RecipeViewModel : NavigationPageViewModelBase
{
    private readonly IRecipeViewCrudService _crudService;
    private readonly IRecipeService _recipeService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaDialogService _dialogService;
    private readonly IAvaloniaDispatcherService _dispatcherService;
    private readonly AsyncRelayCommand _syncCloudCommand;
    private readonly AsyncRelayCommand _switchSourceCommand;
    private readonly AsyncRelayCommand _saveLocalParamCommand;
    private bool _isSubscribed;

    public RecipeViewModel(
        IRecipeViewCrudService crudService,
        IRecipeService recipeService,
        IAvaloniaLanguageService languageService,
        IAvaloniaDialogService dialogService,
        IAvaloniaDispatcherService dispatcherService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _crudService = crudService;
        _recipeService = recipeService;
        _languageService = languageService;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        _syncCloudCommand = new AsyncRelayCommand(SyncCloudAsync);
        _switchSourceCommand = new AsyncRelayCommand(SwitchSourceAsync);
        _saveLocalParamCommand = new AsyncRelayCommand(SaveLocalParamAsync, () => IsLocalAdmin);
        SourceLabel = Text("Navigation_Recipe_NotLoaded", "未加载配方");
    }

    public ObservableCollection<RecipeParamRow> Params { get; } = [];

    [ObservableProperty]
    private string recipeName = string.Empty;

    [ObservableProperty]
    private string recipeVersion = string.Empty;

    [ObservableProperty]
    private string processName = string.Empty;

    [ObservableProperty]
    private string updatedAt = string.Empty;

    [ObservableProperty]
    private bool isCloudSource;

    [ObservableProperty]
    private string sourceLabel = string.Empty;

    [ObservableProperty]
    private bool isLocalAdmin;

    [ObservableProperty]
    private string editKey = string.Empty;

    [ObservableProperty]
    private string editMin = string.Empty;

    [ObservableProperty]
    private string editMax = string.Empty;

    [ObservableProperty]
    private string editUnit = string.Empty;

    [ObservableProperty]
    private string feedbackMessage = string.Empty;

    public IAsyncRelayCommand SyncCloudCommand => _syncCloudCommand;

    public IAsyncRelayCommand SwitchSourceCommand => _switchSourceCommand;

    public IAsyncRelayCommand SaveLocalParamCommand => _saveLocalParamCommand;

    public override async Task OnActivatedAsync()
    {
        if (!_isSubscribed)
        {
            _recipeService.RecipeChanged += HandleRecipeChanged;
            _isSubscribed = true;
        }

        await RefreshUiAsync();
    }

    public override Task OnDeactivatedAsync()
    {
        if (_isSubscribed)
        {
            _recipeService.RecipeChanged -= HandleRecipeChanged;
            _isSubscribed = false;
        }

        return Task.CompletedTask;
    }

    partial void OnIsLocalAdminChanged(bool value)
    {
        _saveLocalParamCommand.NotifyCanExecuteChanged();
        foreach (var row in Params)
        {
            row.CanDelete = value;
        }
    }

    partial void OnIsCloudSourceChanged(bool value)
        => SourceLabel = value
            ? Text("Navigation_Recipe_CloudSource", "云端配方")
            : Text("Navigation_Recipe_LocalSource", "本地配方");

    private void HandleRecipeChanged()
        => _dispatcherService.Post(() => _ = RefreshUiAsync());

    private async Task RefreshUiAsync()
    {
        IsLocalAdmin = await _crudService.GetIsLocalAdminAsync();
        var snapshot = await _crudService.GetSnapshotAsync();
        if (snapshot is null)
        {
            RecipeName = Text("Navigation_Recipe_NotLoaded", "未加载配方");
            RecipeVersion = string.Empty;
            ProcessName = string.Empty;
            UpdatedAt = string.Empty;
            IsCloudSource = _recipeService.ActiveSource == RecipeSource.Cloud;
            Params.Clear();
            FeedbackMessage = RecipeName;
            return;
        }

        RecipeName = snapshot.RecipeName;
        RecipeVersion = snapshot.RecipeVersion;
        ProcessName = snapshot.ProcessName;
        UpdatedAt = snapshot.UpdatedAt;
        IsCloudSource = snapshot.IsCloudSource;

        Params.Clear();
        foreach (var item in snapshot.Params)
        {
            Params.Add(new RecipeParamRow(
                item.Name,
                item.Min,
                item.Max,
                item.Unit,
                IsLocalAdmin,
                DeleteLocalParamAsync));
        }

        FeedbackMessage = string.Empty;
    }

    private async Task SyncCloudAsync()
    {
        var success = await _crudService.SyncCloudAsync();
        var message = success
            ? Text("Navigation_Recipe_SyncSuccess", "云端配方已同步")
            : Text("Navigation_Recipe_SyncFailed", "云端配方同步失败或当前未连接云端");
        FeedbackMessage = message;
        await _dialogService.ShowInfoAsync(ViewTitle, message);
        await RefreshUiAsync();
    }

    private async Task SwitchSourceAsync()
    {
        var target = IsCloudSource ? RecipeSource.Local : RecipeSource.Cloud;
        await _crudService.SwitchSourceAsync(target);
        IsCloudSource = target == RecipeSource.Cloud;
        await RefreshUiAsync();
    }

    private async Task SaveLocalParamAsync()
    {
        if (!IsLocalAdmin)
        {
            await _dialogService.ShowInfoAsync(
                ViewTitle,
                Text("Navigation_Param_NoPermission", "当前用户没有参数配置权限"));
            return;
        }

        var key = EditKey.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            await _dialogService.ShowInfoAsync(
                ViewTitle,
                Text("Navigation_Recipe_Validation_ParamNameRequired", "参数名称不能为空"));
            return;
        }

        if (!TryParseOptional(EditMin, out var min))
        {
            await _dialogService.ShowInfoAsync(
                ViewTitle,
                Text("Navigation_Recipe_Validation_MinNumber", "下限必须是数字"));
            return;
        }

        if (!TryParseOptional(EditMax, out var max))
        {
            await _dialogService.ShowInfoAsync(
                ViewTitle,
                Text("Navigation_Recipe_Validation_MaxNumber", "上限必须是数字"));
            return;
        }

        if (min.HasValue && max.HasValue && min.Value > max.Value)
        {
            await _dialogService.ShowInfoAsync(
                ViewTitle,
                Text("Navigation_Recipe_Validation_MinLessEqualMax", "下限不能大于上限"));
            return;
        }

        await _crudService.SaveLocalParamAsync(key, min, max, EditUnit.Trim());
        EditKey = string.Empty;
        EditMin = string.Empty;
        EditMax = string.Empty;
        EditUnit = string.Empty;
        FeedbackMessage = Text("Navigation_Recipe_LocalSaveSuccess", "本地配方参数已保存");
        await RefreshUiAsync();
    }

    private async Task DeleteLocalParamAsync(string key)
    {
        if (!IsLocalAdmin)
        {
            await _dialogService.ShowInfoAsync(
                ViewTitle,
                Text("Navigation_Param_NoPermission", "当前用户没有参数配置权限"));
            return;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            await _dialogService.ShowInfoAsync(
                ViewTitle,
                Text("Navigation_Recipe_SelectLocalParamToDelete", "请选择要删除的本地配方参数"));
            return;
        }

        await _crudService.DeleteLocalParamAsync(key);
        FeedbackMessage = Text("Navigation_Recipe_LocalDeleteSuccess", "本地配方参数已删除");
        await RefreshUiAsync();
    }

    private static bool TryParseOptional(string text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var current)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out current))
        {
            value = current;
            return true;
        }

        return false;
    }

    private string Text(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
