using System.Collections.ObjectModel;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;
using AvaloniaDispatcherTimer = Avalonia.Threading.DispatcherTimer;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Equipment.Models;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public class EquipmentViewModel : PresentationViewModelBase
{
    private readonly IEquipmentPanelService _equipmentPanelService;
    private readonly IRecipeService _recipeService;
    private readonly AvaloniaDispatcherTimer _hwRefreshTimer;
    private int _selectedTabIndex;
    private string _recipeName = "未加载配方";
    private string _recipeVersion = "--";
    private string _processName = "--";
    private bool _isRecipeActive;
    private int _todayOutput;
    private string _todayYield = "0.00%";
    private int _ngCount;
    private string _currentBatch = "--";

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

    public string RecipeName { get => _recipeName; set { _recipeName = value; OnPropertyChanged(); } }
    public string RecipeVersion { get => _recipeVersion; set { _recipeVersion = value; OnPropertyChanged(); } }
    public string ProcessName { get => _processName; set { _processName = value; OnPropertyChanged(); } }
    public bool IsRecipeActive { get => _isRecipeActive; set { _isRecipeActive = value; OnPropertyChanged(); } }
    public int TodayOutput { get => _todayOutput; set { _todayOutput = value; OnPropertyChanged(); } }
    public string TodayYield { get => _todayYield; set { _todayYield = value; OnPropertyChanged(); } }
    public int NgCount { get => _ngCount; set { _ngCount = value; OnPropertyChanged(); } }
    public string CurrentBatch { get => _currentBatch; set { _currentBatch = value; OnPropertyChanged(); } }

    public EquipmentViewModel(IEquipmentPanelService equipmentPanelService, IRecipeService recipeService)
    {
        _equipmentPanelService = equipmentPanelService;
        _recipeService = recipeService;

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
                RecipeName = "未加载配方";
                RecipeVersion = "--";
                ProcessName = "--";
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
