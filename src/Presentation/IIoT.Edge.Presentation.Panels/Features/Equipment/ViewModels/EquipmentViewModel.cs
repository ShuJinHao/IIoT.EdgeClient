using System.Collections.ObjectModel;
using System.Windows.Input;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;
using AvaloniaDispatcherTimer = Avalonia.Threading.DispatcherTimer;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Recipe;
using IIoT.Edge.Module.Contracts.Production;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public class EquipmentViewModel : PresentationViewModelBase
{
    private const string EmptyDisplayText = "—";

    private readonly IEquipmentPanelService _equipmentPanelService;
    private readonly IRecipeService _recipeService;
    private readonly IProductionPlanSelectionServiceResolver _planSelectionServiceResolver;
    private readonly IProductionPlanSelectionPopupService _planSelectionPopupService;
    private readonly IAppLanguageService _languageService;
    private readonly IDeviceSelectionService _deviceSelectionService;
    private readonly IViewRegistry _viewRegistry;
    private readonly IReadOnlyDictionary<string, IEdgeProcessModule> _processModulesById;
    private readonly AvaloniaDispatcherTimer _hwRefreshTimer;
    private int _selectedTabIndex;
    private string _recipeName = EmptyDisplayText;
    private string _recipeVersion = EmptyDisplayText;
    private string _processName = EmptyDisplayText;
    private bool _isRecipeActive;
    private string _currentBatch = EmptyDisplayText;
    private bool _isMesPlanSelectionRequired;
    private ProductionPlanOption? _selectedProductionPlan;
    private DeviceSelectionOption? _selectedDeviceFilter;
    private bool _isApplyingDeviceSelection;
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
    public ObservableCollection<DeviceSelectionOption> DeviceFilters { get; } = [];

    public bool HasHardwareItems => HardwareItems.Count > 0;
    public bool IsHardwareEmpty => !HasHardwareItems;
    public bool HasRecipeParameters => Parameters.Count > 0;
    public bool IsRecipeParametersEmpty => !HasRecipeParameters;

    public string RecipeName { get => _recipeName; set { _recipeName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayRecipeName)); } }
    public string RecipeVersion { get => _recipeVersion; set { _recipeVersion = value; OnPropertyChanged(); } }
    public string ProcessName { get => _processName; set { _processName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayProcessName)); OnPropertyChanged(nameof(CurrentProcessDisplayName)); } }
    public bool IsRecipeActive { get => _isRecipeActive; set { _isRecipeActive = value; OnPropertyChanged(); } }
    public string CurrentBatch { get => _currentBatch; set { _currentBatch = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayCurrentBatch)); } }
    public string DisplayRecipeName => NormalizeDisplayText(RecipeName);
    public string DisplayProcessName => NormalizeDisplayText(ProcessName);
    public string CurrentProcessDisplayName => ResolveCurrentProcessDisplayName();
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

    public DeviceSelectionOption? SelectedDeviceFilter
    {
        get => _selectedDeviceFilter;
        set
        {
            if (Equals(_selectedDeviceFilter, value))
            {
                return;
            }

            _selectedDeviceFilter = value;
            OnPropertyChanged();
            if (!_isApplyingDeviceSelection)
            {
                _deviceSelectionService.SelectDevice(value?.Key ?? IDeviceSelectionService.AllFilterKey);
            }
        }
    }

    public ICommand SelectProductionPlanCommand { get; }

    public EquipmentViewModel(
        IEquipmentPanelService equipmentPanelService,
        IRecipeService recipeService,
        IProductionPlanSelectionServiceResolver planSelectionServiceResolver,
        IProductionPlanSelectionPopupService planSelectionPopupService,
        IAppLanguageService languageService,
        IDeviceSelectionService deviceSelectionService,
        IViewRegistry viewRegistry,
        IEnumerable<IEdgeProcessModule> processModules)
    {
        _equipmentPanelService = equipmentPanelService;
        _recipeService = recipeService;
        _planSelectionServiceResolver = planSelectionServiceResolver;
        _planSelectionPopupService = planSelectionPopupService;
        _languageService = languageService;
        _deviceSelectionService = deviceSelectionService;
        _viewRegistry = viewRegistry;
        _processModulesById = processModules
            .Where(static module => !string.IsNullOrWhiteSpace(module.ModuleId))
            .GroupBy(static module => module.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        SelectProductionPlanCommand = new AsyncCommand(
            () => RunViewTaskAsync(
                SelectProductionPlanCoreAsync,
                _languageService.GetString("Panels_Error_SelectProductionPlanFailed", "选择主批计划失败")));

        LayoutRow = 1;
        LayoutColumn = 1;

        _recipeService.RecipeChanged += RefreshRecipe;
        _languageService.LanguageChanged += OnLanguageChanged;
        _deviceSelectionService.SelectionChanged += OnSharedDeviceSelectionChanged;
        SyncDeviceSelectionOptions([]);

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

    private async Task LoadPanelAsync()
    {
        await RefreshHardwareAsync();
        await RefreshRecipeAsync();
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
            SyncDeviceSelectionOptions(snapshots);
            NotifyHardwareStateChanged();
        });
    }

    private void SyncDeviceSelectionOptions(IReadOnlyCollection<HardwareSnapshot> snapshots)
    {
        var preferredKey = _deviceSelectionService.SelectedDeviceKey;
        var plcSnapshots = snapshots
            .Where(static snapshot =>
                string.Equals(snapshot.DeviceType, "PLC", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _deviceSelectionService.UpdatePlcIdentities(
            plcSnapshots.Select(static snapshot =>
                new PlcDeviceSelectionIdentity(snapshot.Name, snapshot.PlcCode)));
        var preferredPlcCode = _deviceSelectionService.SelectedPlcCode;
        var options = new List<DeviceSelectionOption>
        {
            CreateAllDeviceOption()
        };

        options.AddRange(
            plcSnapshots
                .Select(static snapshot => CreateDeviceSelectionOption(snapshot))
                .Where(static option => option is not null)
                .Select(static option => option!)
                .GroupBy(static option => option.Key, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Take(2).Count() == 1)
                .Select(static group => group.First())
                .OrderBy(static option => option.DisplayName, StringComparer.OrdinalIgnoreCase));

        if (!string.Equals(preferredKey, IDeviceSelectionService.AllFilterKey, StringComparison.OrdinalIgnoreCase)
            && options.All(option => !string.Equals(option.Key, preferredKey, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(preferredPlcCode)
                || options.All(option => !string.Equals(
                    option.PlcCode,
                    preferredPlcCode,
                    StringComparison.OrdinalIgnoreCase))))
        {
            options.Add(new DeviceSelectionOption(preferredKey, preferredKey));
        }

        ReplaceItems(DeviceFilters, options);
        ApplySelectedDevice(preferredKey, preferredPlcCode);
    }

    private static DeviceSelectionOption? CreateDeviceSelectionOption(HardwareSnapshot snapshot)
    {
        var deviceName = snapshot.Name?.Trim() ?? string.Empty;
        var plcCode = snapshot.PlcCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        var displayName = string.IsNullOrWhiteSpace(plcCode)
                          || string.Equals(plcCode, deviceName, StringComparison.OrdinalIgnoreCase)
            ? deviceName
            : $"{plcCode} · {deviceName}";
        return new DeviceSelectionOption(deviceName, displayName)
        {
            PlcCode = plcCode
        };
    }

    private DeviceSelectionOption CreateAllDeviceOption()
        => new(
            IDeviceSelectionService.AllFilterKey,
            _languageService.GetString("Panels_Filter_AllOrSummary", "全部/汇总"));

    private void OnSharedDeviceSelectionChanged(object? sender, EventArgs e)
        => AvaloniaDispatcher.UIThread.Post(
            () => ApplySelectedDevice(
                _deviceSelectionService.SelectedDeviceKey,
                _deviceSelectionService.SelectedPlcCode));

    private void ApplySelectedDevice(string selectedKey, string? selectedPlcCode = null)
    {
        var option = DeviceFilters.FirstOrDefault(filter =>
                string.Equals(filter.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
            ?? DeviceFilters.FirstOrDefault(filter =>
                !string.IsNullOrWhiteSpace(selectedPlcCode)
                && string.Equals(
                    filter.PlcCode,
                    selectedPlcCode,
                    StringComparison.OrdinalIgnoreCase))
            ?? DeviceFilters.FirstOrDefault();

        _isApplyingDeviceSelection = true;
        try
        {
            SelectedDeviceFilter = option;
        }
        finally
        {
            _isApplyingDeviceSelection = false;
        }
    }

    private void RefreshRecipe()
        => RunOnUiThread(() => RunViewTaskInBackground(RefreshRecipeAsync, "刷新配方信息失败"));

    private void OnLanguageChanged(object? sender, EventArgs e)
        => RunOnUiThread(() =>
        {
            RefreshAllDeviceOptionLanguage();
            OnPropertyChanged(nameof(CurrentProcessDisplayName));
        });

    private static void RunOnUiThread(Action action)
    {
        if (AvaloniaDispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        AvaloniaDispatcher.UIThread.Post(action);
    }

    private void RefreshAllDeviceOptionLanguage()
    {
        for (var index = 0; index < DeviceFilters.Count; index++)
        {
            if (!string.Equals(
                    DeviceFilters[index].Key,
                    IDeviceSelectionService.AllFilterKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DeviceFilters[index] = CreateAllDeviceOption();
            ApplySelectedDevice(_deviceSelectionService.SelectedDeviceKey);
            return;
        }
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
            ReplaceItems(
                Parameters,
                snapshot.Parameters.Select(parameter => new RecipeParamViewModel
                {
                    ParamName = parameter.ParamName,
                    CurrentValue = parameter.CurrentValue,
                    MinValue = parameter.MinValue,
                    MaxValue = parameter.MaxValue,
                    Unit = parameter.Unit,
                    WarnLow = parameter.WarnLow,
                    WarnHigh = parameter.WarnHigh
                }));
            NotifyRecipeParameterStateChanged();
        });
    }

    private async Task RefreshProductionPlanStateAsync()
    {
        var planSelectionService = _planSelectionServiceResolver.ResolveCurrent();
        if (planSelectionService is null)
        {
            IsMesPlanSelectionRequired = false;
            SelectedProductionPlan = null;
            TraceBatchNumber = EmptyDisplayText;
            TraceBatchError = string.Empty;
            return;
        }

        var state = await planSelectionService.GetStateAsync();
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
        var planSelectionService = _planSelectionServiceResolver.ResolveCurrent();
        if (planSelectionService is null)
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
            await planSelectionService.SelectPlanAsync(selected);
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

    private string ResolveCurrentProcessDisplayName()
    {
        var currentProcess = NormalizeDisplayText(ProcessName);
        if (currentProcess != EmptyDisplayText)
        {
            return currentProcess;
        }

        var dataViewTitles = _viewRegistry.GetAllMenus()
            .Where(static menu => menu.ViewId.EndsWith(".DataView", StringComparison.OrdinalIgnoreCase))
            .Select(ResolveCurrentProcessFallbackTitle)
            .Where(static title => title != EmptyDisplayText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (dataViewTitles.Length == 1)
        {
            return dataViewTitles[0];
        }

        if (dataViewTitles.Length > 1)
        {
            return _languageService.GetString("Panels_Status_ProcessAmbiguous", "未确定");
        }

        return _languageService.GetString("Panels_Status_ProcessNotConfigured", "未配置工序");
    }

    private string ResolveCurrentProcessFallbackTitle(MenuInfo menu)
    {
        var title = ResolveMenuTitle(menu);
        if (!IsGenericDataViewTitle(title, menu.TitleResourceKey))
        {
            return title;
        }

        var moduleDisplayName = ResolveModuleDisplayName(menu.ViewId);
        return moduleDisplayName == EmptyDisplayText
            ? title
            : moduleDisplayName;
    }

    private string ResolveMenuTitle(MenuInfo menu)
    {
        var title = string.IsNullOrWhiteSpace(menu.TitleResourceKey)
            ? menu.Title
            : _languageService.GetString(menu.TitleResourceKey, menu.Title);

        return NormalizeDisplayText(title);
    }

    private string ResolveModuleDisplayName(string viewId)
    {
        if (string.IsNullOrWhiteSpace(viewId))
        {
            return EmptyDisplayText;
        }

        var separatorIndex = viewId.IndexOf('.');
        var moduleId = separatorIndex > 0
            ? viewId[..separatorIndex]
            : viewId;
        if (!_processModulesById.TryGetValue(moduleId, out var module))
        {
            return EmptyDisplayText;
        }

        return NormalizeDisplayText(module.DisplayName);
    }

    private static bool IsGenericDataViewTitle(string title, string titleResourceKey)
    {
        if (string.IsNullOrWhiteSpace(title) || title == EmptyDisplayText)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(titleResourceKey)
            && titleResourceKey.EndsWith("_Menu_Data", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return title.Trim() switch
        {
            "数据" => true,
            "Data" => true,
            "生产数据" => true,
            "Production Data" => true,
            "实时数据" => true,
            "Realtime Data" => true,
            _ => false
        };
    }

    private string ResolveTraceBatchNumber(ProductionPlanSelectionState state)
    {
        if (state.HasTraceBatchNumber)
        {
            return NormalizeDisplayText(state.TraceBatchNumber);
        }

        if (state.HasSelectedPlan
            && state.TraceBatchError == ProductionPlanSelectionErrorCodes.TraceBatchNumberMissing)
        {
            return _languageService.GetString("Panels_Status_TraceBatchNotGenerated", "MES未生成");
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
