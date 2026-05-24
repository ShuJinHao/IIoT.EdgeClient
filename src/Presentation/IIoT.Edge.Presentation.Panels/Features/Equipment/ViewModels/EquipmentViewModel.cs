using System.Collections.ObjectModel;
using System.Windows.Input;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;
using AvaloniaDispatcherTimer = Avalonia.Threading.DispatcherTimer;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Equipment.Models;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public class EquipmentViewModel : PresentationViewModelBase
{
    private const string EmptyDisplayText = "—";

    private readonly IEquipmentPanelService _equipmentPanelService;
    private readonly IRecipeService _recipeService;
    private readonly IProductionPlanSelectionService? _planSelectionService;
    private readonly IProductionPlanSelectionPopupService _planSelectionPopupService;
    private readonly IAppLanguageService _languageService;
    private readonly AvaloniaDispatcherTimer _hwRefreshTimer;
    private int _selectedTabIndex;
    private string _recipeName = EmptyDisplayText;
    private string _recipeVersion = EmptyDisplayText;
    private string _processName = EmptyDisplayText;
    private bool _isRecipeActive;
    private int _todayOutput;
    private string _todayYield = "0.00%";
    private int _ngCount;
    private string _currentBatch = EmptyDisplayText;
    private bool _isMesPlanSelectionRequired;
    private ProductionPlanOption? _selectedProductionPlan;
    private string _traceBatchNumber = EmptyDisplayText;
    private string _traceBatchError = string.Empty;

    public override string ViewId => "Core.Equipment";
    public override string ViewTitle => "设备运行";

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    public ObservableCollection<HardwareItemViewModel> HardwareItems { get; } = new();
    public ObservableCollection<RecipeParamViewModel> Parameters { get; } = new();

    public bool HasHardwareItems => HardwareItems.Count > 0;
    public bool IsHardwareEmpty => !HasHardwareItems;
    public bool HasRecipeParameters => Parameters.Count > 0;
    public bool IsRecipeParametersEmpty => !HasRecipeParameters;

    public string RecipeName { get => _recipeName; set { _recipeName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayRecipeName)); } }
    public string RecipeVersion { get => _recipeVersion; set { _recipeVersion = value; OnPropertyChanged(); } }
    public string ProcessName { get => _processName; set { _processName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayProcessName)); } }
    public bool IsRecipeActive { get => _isRecipeActive; set { _isRecipeActive = value; OnPropertyChanged(); } }
    public int TodayOutput { get => _todayOutput; set { _todayOutput = value; OnPropertyChanged(); } }
    public string TodayYield { get => _todayYield; set { _todayYield = value; OnPropertyChanged(); } }
    public int NgCount { get => _ngCount; set { _ngCount = value; OnPropertyChanged(); } }
    public string CurrentBatch { get => _currentBatch; set { _currentBatch = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayCurrentBatch)); } }
    public string DisplayRecipeName => NormalizeDisplayText(RecipeName);
    public string DisplayProcessName => NormalizeDisplayText(ProcessName);
    public string DisplayCurrentBatch => NormalizeDisplayText(CurrentBatch);
    public bool IsMesPlanSelectionRequired
    {
        get => _isMesPlanSelectionRequired;
        set { _isMesPlanSelectionRequired = value; OnPropertyChanged(); }
    }

    public ProductionPlanOption? SelectedProductionPlan
    {
        get => _selectedProductionPlan;
        set
        {
            _selectedProductionPlan = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedProductionPlan));
            OnPropertyChanged(nameof(SelectedPlanCode));
            OnPropertyChanged(nameof(SelectedPlanWorkOrder));
            OnPropertyChanged(nameof(SelectedPlanProduct));
            OnPropertyChanged(nameof(SelectedPlanProductSummary));
            OnPropertyChanged(nameof(SelectedPlanQuantity));
            OnPropertyChanged(nameof(SelectedPlanStatus));
        }
    }

    public bool HasSelectedProductionPlan => SelectedProductionPlan is not null;
    public string SelectedPlanCode => NormalizeDisplayText(SelectedProductionPlan?.DisplayPlanCode);
    public string SelectedPlanWorkOrder => NormalizeDisplayText(SelectedProductionPlan?.DisplayWorkOrder);
    public string SelectedPlanProduct => NormalizeDisplayText(SelectedProductionPlan?.DisplayProduct);
    public string SelectedPlanProductSummary
    {
        get
        {
            var product = NormalizeDisplayText(SelectedProductionPlan?.DisplayProduct);
            var workOrder = NormalizeDisplayText(SelectedProductionPlan?.DisplayWorkOrder);

            if (product == EmptyDisplayText)
            {
                return workOrder;
            }

            return workOrder == EmptyDisplayText
                ? product
                : $"{product} / {workOrder}";
        }
    }

    public string SelectedPlanQuantity => NormalizeDisplayText(SelectedProductionPlan?.DisplayQuantity);
    public string SelectedPlanStatus => NormalizeDisplayText(SelectedProductionPlan?.PlanStatus);
    public string TraceBatchNumber
    {
        get => _traceBatchNumber;
        set
        {
            _traceBatchNumber = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTraceBatchError));
        }
    }

    public string TraceBatchError
    {
        get => _traceBatchError;
        set
        {
            _traceBatchError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTraceBatchError));
        }
    }

    public bool HasTraceBatchError => !string.IsNullOrWhiteSpace(TraceBatchError);
    public ICommand SelectProductionPlanCommand { get; }

    public EquipmentViewModel(
        IEquipmentPanelService equipmentPanelService,
        IRecipeService recipeService,
        IEnumerable<IProductionPlanSelectionService> planSelectionServices,
        IProductionPlanSelectionPopupService planSelectionPopupService,
        IAppLanguageService languageService)
    {
        _equipmentPanelService = equipmentPanelService;
        _recipeService = recipeService;
        _planSelectionService = planSelectionServices.FirstOrDefault();
        _planSelectionPopupService = planSelectionPopupService;
        _languageService = languageService;
        SelectProductionPlanCommand = new AsyncCommand(
            () => RunViewTaskAsync(
                SelectProductionPlanCoreAsync,
                _languageService.GetString("Panels_Error_SelectProductionPlanFailed", "选择主批计划失败")));

        LayoutRow = 1;
        LayoutColumn = 1;

        _recipeService.RecipeChanged += RefreshRecipe;

        _hwRefreshTimer = new AvaloniaDispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _hwRefreshTimer.Tick += (_, _) => RunViewTaskInBackground(RefreshHardwareAsync, "刷新硬件状态失败");
    }

    public override async Task OnActivatedAsync()
    {
        await RunViewTaskAsync(LoadPanelAsync, "加载设备面板失败");
        if (!_hwRefreshTimer.IsEnabled)
        {
            _hwRefreshTimer.Start();
        }
    }

    public override Task OnDeactivatedAsync()
    {
        _hwRefreshTimer.Stop();
        return Task.CompletedTask;
    }

    public void OnCapacityUpdated() => RunViewTaskInBackground(RefreshCapacityAsync, "刷新产量摘要失败");

    private async Task LoadPanelAsync()
    {
        await RefreshHardwareAsync();
        await RefreshRecipeAsync();
        await RefreshCapacityAsync();
        await RefreshProductionPlanStateAsync();
    }

    private async Task RefreshHardwareAsync()
    {
        var snapshots = await _equipmentPanelService.GetHardwareStatusAsync();

        await AvaloniaDispatcher.UIThread.InvokeAsync(() =>
        {
            SyncItemsByKey(
                HardwareItems,
                snapshots,
                item => item.Name,
                snapshot => snapshot.Name,
                snapshot => new HardwareItemViewModel
                {
                    Name = snapshot.Name,
                    Address = snapshot.Address,
                    DeviceType = snapshot.DeviceType,
                    IsConnected = snapshot.IsConnected
                },
                (item, snapshot) =>
                {
                    item.Address = snapshot.Address;
                    item.IsConnected = snapshot.IsConnected;
                });
            NotifyHardwareStateChanged();
        });
    }

    private void RefreshRecipe()
    {
        RunViewTaskInBackground(RefreshRecipeAsync, "刷新配方信息失败");
    }

    private async Task RefreshRecipeAsync()
    {
        var snapshot = await _equipmentPanelService.GetRecipeSnapshotAsync();

        await AvaloniaDispatcher.UIThread.InvokeAsync(() =>
        {
            if (snapshot is null)
            {
                RecipeName = EmptyDisplayText;
                RecipeVersion = EmptyDisplayText;
                ProcessName = EmptyDisplayText;
                IsRecipeActive = false;
                ReplaceItems<RecipeParamViewModel>(Parameters, Array.Empty<RecipeParamViewModel>());
                NotifyRecipeParameterStateChanged();
                return;
            }

            RecipeName = snapshot.RecipeName;
            RecipeVersion = snapshot.RecipeVersion;
            ProcessName = snapshot.ProcessName;
            IsRecipeActive = snapshot.IsRecipeActive;
            ReplaceItems<RecipeParamViewModel>(Parameters, snapshot.Parameters);
            NotifyRecipeParameterStateChanged();
        });
    }

    private async Task RefreshCapacityAsync()
    {
        var snapshot = await _equipmentPanelService.GetCapacitySnapshotAsync();

        await AvaloniaDispatcher.UIThread.InvokeAsync(() =>
        {
            TodayOutput = snapshot.TodayOutput;
            NgCount = snapshot.NgCount;
            TodayYield = snapshot.TodayYield;
            CurrentBatch = snapshot.CurrentBatch;
        });
    }

    private async Task RefreshProductionPlanStateAsync()
    {
        if (_planSelectionService is null)
        {
            IsMesPlanSelectionRequired = false;
            SelectedProductionPlan = null;
            TraceBatchNumber = EmptyDisplayText;
            TraceBatchError = string.Empty;
            return;
        }

        var state = await _planSelectionService.GetStateAsync();
        await AvaloniaDispatcher.UIThread.InvokeAsync(() =>
        {
            IsMesPlanSelectionRequired = state.RequiresSelection;
            SelectedProductionPlan = state.CurrentPlan;
            TraceBatchNumber = ResolveTraceBatchNumber(state);
            TraceBatchError = ResolveProductionPlanMessage(state.TraceBatchError);
        });
    }

    private async Task SelectProductionPlanCoreAsync()
    {
        if (_planSelectionService is null)
        {
            return;
        }

        var selected = await _planSelectionPopupService.ShowAsync();
        if (selected is null)
        {
            return;
        }

        await ApplySelectedProductionPlanPreviewAsync(selected);

        try
        {
            await _planSelectionService.SelectPlanAsync(selected);
            await RefreshProductionPlanStateAsync();
        }
        catch (Exception ex)
        {
            await RefreshProductionPlanStateAsync();
            await PreserveSelectedProductionPlanAfterFailureAsync(selected, ex.Message);
            throw;
        }
    }

    private async Task ApplySelectedProductionPlanPreviewAsync(ProductionPlanOption selected)
    {
        await AvaloniaDispatcher.UIThread.InvokeAsync(() =>
        {
            SelectedProductionPlan = selected;
            TraceBatchNumber = _languageService.GetString("Panels_Status_TraceBatchPending", "待生成");
            TraceBatchError = string.Empty;
        });
    }

    private async Task PreserveSelectedProductionPlanAfterFailureAsync(ProductionPlanOption selected, string? errorMessage)
    {
        await AvaloniaDispatcher.UIThread.InvokeAsync(() =>
        {
            SelectedProductionPlan ??= selected;

            if (string.IsNullOrWhiteSpace(TraceBatchNumber) || TraceBatchNumber == EmptyDisplayText)
            {
                TraceBatchNumber = _languageService.GetString("Panels_Status_TraceBatchPending", "待生成");
            }

            if (string.IsNullOrWhiteSpace(TraceBatchError))
            {
                TraceBatchError = ResolveProductionPlanMessage(errorMessage);
            }
        });
    }

    private static string NormalizeDisplayText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Trim() == "--"
            ? EmptyDisplayText
            : value;
    }

    private string ResolveTraceBatchNumber(ProductionPlanSelectionState state)
    {
        if (state.HasTraceBatchNumber)
        {
            return NormalizeDisplayText(state.TraceBatchNumber);
        }

        return state.HasSelectedPlan
            ? _languageService.GetString("Panels_Status_TraceBatchPending", "待生成")
            : EmptyDisplayText;
    }

    private string ResolveProductionPlanMessage(string? codeOrMessage)
        => codeOrMessage switch
        {
            ProductionPlanSelectionErrorCodes.MissingOperationCode => _languageService.GetString(
                "Panels_Error_MesOperationCodeMissing",
                "MES 工序编码未配置。"),
            ProductionPlanSelectionErrorCodes.MissingMainPlanCode => _languageService.GetString(
                "Panels_Error_MesMainPlanCodeMissing",
                "主批计划号缺失。"),
            ProductionPlanSelectionErrorCodes.TraceBatchTimeout => _languageService.GetString(
                "Panels_Error_TraceBatchTimeout",
                "追溯批次号生成超时，请检查 MES 接口或当前主批计划。"),
            ProductionPlanSelectionErrorCodes.TraceBatchNumberMissing => _languageService.GetString(
                "Panels_Error_TraceBatchNumberMissing",
                "MES 未返回追溯批次号。"),
            null or "" => string.Empty,
            _ => codeOrMessage
        };

    private void NotifyHardwareStateChanged()
    {
        OnPropertyChanged(nameof(HasHardwareItems));
        OnPropertyChanged(nameof(IsHardwareEmpty));
    }

    private void NotifyRecipeParameterStateChanged()
    {
        OnPropertyChanged(nameof(HasRecipeParameters));
        OnPropertyChanged(nameof(IsRecipeParametersEmpty));
    }
}
