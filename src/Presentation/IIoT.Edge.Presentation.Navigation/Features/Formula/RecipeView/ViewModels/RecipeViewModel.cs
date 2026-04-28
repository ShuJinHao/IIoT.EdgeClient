using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.SharedKernel.DataPipeline.Recipe;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;

public class RecipeViewModel : LocalizedCrudPageViewModelBase
{
    private readonly IRecipeViewCrudService _crudService;
    private readonly IRecipeService _recipeService;
    private readonly IEditorValidator<LocalRecipeParamEditModel> _localRecipeParamValidator = new LocalRecipeParamValidator();
    private bool _isSubscribed;

    public ObservableCollection<RecipeParamVm> Params { get; } = new();

    private string _recipeName = "";
    private string _recipeVersion = "";
    private string _processName = "";
    private string _updatedAt = "";

    public string RecipeName { get => _recipeName; set { _recipeName = value; OnPropertyChanged(); } }
    public string RecipeVersion { get => _recipeVersion; set { _recipeVersion = value; OnPropertyChanged(); } }
    public string ProcessName { get => _processName; set { _processName = value; OnPropertyChanged(); } }
    public string UpdatedAt { get => _updatedAt; set { _updatedAt = value; OnPropertyChanged(); } }

    private bool _isCloudSource = true;
    public bool IsCloudSource
    {
        get => _isCloudSource;
        set
        {
            _isCloudSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SourceLabel));
        }
    }

    public string SourceLabel => IsCloudSource
        ? GetText("Navigation_Recipe_CloudSource", "云端配方")
        : GetText("Navigation_Recipe_LocalSource", "本地配方");

    private bool _isLocalAdmin;
    public bool IsLocalAdmin
    {
        get => _isLocalAdmin;
        set { _isLocalAdmin = value; OnPropertyChanged(); }
    }

    private string _editKey = "";
    private string _editMin = "";
    private string _editMax = "";
    private string _editUnit = "";

    public string EditKey { get => _editKey; set { _editKey = value; OnPropertyChanged(); } }
    public string EditMin { get => _editMin; set { _editMin = value; OnPropertyChanged(); } }
    public string EditMax { get => _editMax; set { _editMax = value; OnPropertyChanged(); } }
    public string EditUnit { get => _editUnit; set { _editUnit = value; OnPropertyChanged(); } }

    public ICommand SyncCloudCommand { get; }
    public ICommand SwitchSourceCommand { get; }
    public ICommand SaveLocalParamCommand { get; }
    public ICommand DeleteLocalParamCommand { get; }

    public RecipeViewModel(
        IRecipeViewCrudService crudService,
        IRecipeService recipeService,
        IAppLanguageService languageService)
        : this(
            crudService,
            recipeService,
            languageService,
            "Formula.RecipeView",
            "Navigation_Title_ProductRecipe",
            "产品配方")
    {
    }

    public RecipeViewModel(
        IRecipeViewCrudService crudService,
        IRecipeService recipeService,
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _crudService = crudService;
        _recipeService = recipeService;

        SyncCloudCommand = CreateBusyCommand(OnSyncCloudAsync);
        SwitchSourceCommand = new BaseCommand(_ => OnSwitchSource());
        SaveLocalParamCommand = CreateBusyCommand(OnSaveLocalParamAsync, () => IsLocalAdmin);
        DeleteLocalParamCommand = new BaseCommand(
            param => _ = RunDeleteAsync(() => OnDeleteLocalParamAsync(param)),
            param => IsLocalAdmin && param is string key && !string.IsNullOrWhiteSpace(key));
    }

    public override async Task OnActivatedAsync()
    {
        EnsureSubscriptions();
        await ExecuteBusyAsync(async () =>
        {
            await UpdateAdminStateAsync();
            IsCloudSource = _recipeService.ActiveSource == RecipeSource.Cloud;
            await RefreshUIAsync();
        });
    }

    public override Task OnDeactivatedAsync()
    {
        RemoveSubscriptions();
        return Task.CompletedTask;
    }

    private async Task<CrudOperationResult> OnSyncCloudAsync()
    {
        var success = await _crudService.SyncCloudAsync();
        if (!success)
        {
            return CrudOperationResult.Failure(GetText("Navigation_Recipe_SyncFailed", "配方同步失败，请检查网络连接。"));
        }

        await RefreshUIAsync();
        return CrudOperationResult.Success(GetText("Navigation_Recipe_SyncSuccess", "云端配方已同步。"));
    }

    private void OnSwitchSource()
    {
        var newSource = IsCloudSource ? RecipeSource.Local : RecipeSource.Cloud;
        _ = _crudService.SwitchSourceAsync(newSource);
        IsCloudSource = newSource == RecipeSource.Cloud;
    }

    private async Task<CrudOperationResult> OnSaveLocalParamAsync()
    {
        var editModel = new LocalRecipeParamEditModel(EditKey, EditMin, EditMax, EditUnit);
        var validationIssues = await ValidateAsync(editModel, _localRecipeParamValidator);
        var validationResult = CreateValidationResult(validationIssues);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        double? min = double.TryParse(EditMin, out var minVal) ? minVal : null;
        double? max = double.TryParse(EditMax, out var maxVal) ? maxVal : null;

        await _crudService.SaveLocalParamAsync(
            EditKey.Trim(),
            min,
            max,
            EditUnit.Trim());

        EditKey = "";
        EditMin = "";
        EditMax = "";
        EditUnit = "";

        await RefreshUIAsync();
        return CrudOperationResult.Success(GetText("Navigation_Recipe_LocalSaveSuccess", "本地配方参数已保存。"));
    }

    private async Task<CrudOperationResult> OnDeleteLocalParamAsync(object? param)
    {
        if (param is not string key || string.IsNullOrWhiteSpace(key))
        {
            return CrudOperationResult.Failure(GetText("Navigation_Recipe_SelectLocalParamToDelete", "请选择要删除的本地配方参数。"));
        }

        await _crudService.DeleteLocalParamAsync(key);
        await RefreshUIAsync();
        return CrudOperationResult.Success(GetText("Navigation_Recipe_LocalDeleteSuccess", "本地配方参数已删除。"));
    }

    private async Task UpdateAdminStateAsync()
    {
        IsLocalAdmin = await _crudService.GetIsLocalAdminAsync();
    }

    private void RefreshUI()
    {
        _ = RefreshUIAsync();
    }

    private void EnsureSubscriptions()
    {
        if (_isSubscribed)
        {
            return;
        }

        _recipeService.RecipeChanged += RefreshUI;
        _recipeService.RecipeChanged += OnRecipeChangedUpdateAdminState;
        _isSubscribed = true;
    }

    private void RemoveSubscriptions()
    {
        if (!_isSubscribed)
        {
            return;
        }

        _recipeService.RecipeChanged -= RefreshUI;
        _recipeService.RecipeChanged -= OnRecipeChangedUpdateAdminState;
        _isSubscribed = false;
    }

    private void OnRecipeChangedUpdateAdminState()
    {
        _ = UpdateAdminStateAsync();
    }

    private async Task RefreshUIAsync()
    {
        var snapshot = await _crudService.GetSnapshotAsync();

        if (snapshot is null)
        {
            RecipeName = GetText("Navigation_Recipe_NotLoaded", "未加载");
            RecipeVersion = "";
            ProcessName = "";
            UpdatedAt = "";
            Params.Clear();
            return;
        }

        RecipeName = snapshot.RecipeName;
        RecipeVersion = snapshot.RecipeVersion;
        ProcessName = snapshot.ProcessName;
        UpdatedAt = snapshot.UpdatedAt;
        IsCloudSource = snapshot.IsCloudSource;

        ReplaceItems(
            Params,
            snapshot.Params.Select(param => new RecipeParamVm
            {
                Name = param.Name,
                Min = param.Min,
                Max = param.Max,
                Unit = param.Unit
            }));
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        OnPropertyChanged(nameof(SourceLabel));
        if (string.IsNullOrWhiteSpace(RecipeName)
            || RecipeName is "未加载" or "Not Loaded")
        {
            RecipeName = GetText("Navigation_Recipe_NotLoaded", "未加载");
        }
    }
}

public class RecipeParamVm
{
    public string Name { get; set; } = "";
    public string Min { get; set; } = "";
    public string Max { get; set; } = "";
    public string Unit { get; set; } = "";
}
