using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Equipment.Models;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public class EquipmentViewModel : PresentationViewModelBase
{
    private readonly IEquipmentPanelService _equipmentPanelService;
    private readonly IRecipeService _recipeService;
    private readonly DispatcherTimer _hwRefreshTimer;
    private int _selectedTabIndex;
    private string _recipeName = "No Recipe";
    private string _recipeVersion = "--";
    private string _processName = "--";
    private bool _isRecipeActive;
    private int _todayOutput;
    private string _todayYield = "0.00%";
    private int _ngCount;
    private string _currentBatch = "--";

    public override string ViewId => "Core.Equipment";
    public override string ViewTitle => "Equipment Info";

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    public ObservableCollection<HardwareItemViewModel> HardwareItems { get; } = new();
    public ObservableCollection<RecipeParamViewModel> Parameters { get; } = new();

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

        _hwRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _hwRefreshTimer.Tick += (_, _) => RunViewTaskInBackground(RefreshHardwareAsync, "Refresh hardware status failed.");

        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            async () => await OnActivatedAsync());
    }

    public override async Task OnActivatedAsync()
    {
        await RunViewTaskAsync(LoadPanelAsync, "Load equipment panel failed.");
        _hwRefreshTimer.Start();
    }

    public void OnCapacityUpdated() => RunViewTaskInBackground(RefreshCapacityAsync, "Refresh capacity summary failed.");

    private async Task LoadPanelAsync()
    {
        await RefreshHardwareAsync();
        await RefreshRecipeAsync();
        await RefreshCapacityAsync();
    }

    private async Task RefreshHardwareAsync()
    {
        var snapshots = await _equipmentPanelService.GetHardwareStatusAsync();

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
        });
    }

    private void RefreshRecipe()
    {
        RunViewTaskInBackground(RefreshRecipeAsync, "Refresh recipe info failed.");
    }

    private async Task RefreshRecipeAsync()
    {
        var snapshot = await _equipmentPanelService.GetRecipeSnapshotAsync();

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (snapshot is null)
            {
                RecipeName = "No Recipe";
                RecipeVersion = "--";
                ProcessName = "--";
                IsRecipeActive = false;
                ReplaceItems<RecipeParamViewModel>(Parameters, Array.Empty<RecipeParamViewModel>());
                return;
            }

            RecipeName = snapshot.RecipeName;
            RecipeVersion = snapshot.RecipeVersion;
            ProcessName = snapshot.ProcessName;
            IsRecipeActive = snapshot.IsRecipeActive;
            ReplaceItems<RecipeParamViewModel>(Parameters, snapshot.Parameters);
        });
    }

    private async Task RefreshCapacityAsync()
    {
        var snapshot = await _equipmentPanelService.GetCapacitySnapshotAsync();

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            TodayOutput = snapshot.TodayOutput;
            NgCount = snapshot.NgCount;
            TodayYield = snapshot.TodayYield;
            CurrentBatch = snapshot.CurrentBatch;
        });
    }
}
