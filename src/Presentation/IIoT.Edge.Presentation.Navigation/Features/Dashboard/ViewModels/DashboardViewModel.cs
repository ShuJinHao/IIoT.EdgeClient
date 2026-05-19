using System.Collections.ObjectModel;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.Dashboard;

public sealed class DashboardViewModel : NavigationViewModelBase
{
    private readonly IEquipmentPanelService _equipmentPanelService;
    private string _connectedDevices = "0 / 0";
    private string _recipeName = "--";
    private string _recipeStatusResourceKey = string.Empty;
    private string _recipeStatusFallback = "--";
    private string _todayOutput = "0";
    private string _todayYield = "0.0%";
    private string _ngCount = "0";
    private string _currentBatch = "--";

    public DashboardViewModel(
        IEquipmentPanelService equipmentPanelService,
        IAppLanguageService languageService)
        : base(languageService, CoreViewIds.Dashboard, "Navigation_Dashboard_Title", "首页总览")
    {
        _equipmentPanelService = equipmentPanelService;
    }

    public ObservableCollection<DashboardDeviceItemViewModel> Devices { get; } = [];

    public string ConnectedDevices
    {
        get => _connectedDevices;
        private set { _connectedDevices = value; OnPropertyChanged(); }
    }

    public string RecipeName
    {
        get => _recipeName;
        private set { _recipeName = value; OnPropertyChanged(); }
    }

    public string RecipeStatus => string.IsNullOrWhiteSpace(_recipeStatusResourceKey)
        ? _recipeStatusFallback
        : GetText(_recipeStatusResourceKey, _recipeStatusFallback);

    public string TodayOutput
    {
        get => _todayOutput;
        private set { _todayOutput = value; OnPropertyChanged(); }
    }

    public string TodayYield
    {
        get => _todayYield;
        private set { _todayYield = value; OnPropertyChanged(); }
    }

    public string NgCount
    {
        get => _ngCount;
        private set { _ngCount = value; OnPropertyChanged(); }
    }

    public string CurrentBatch
    {
        get => _currentBatch;
        private set { _currentBatch = value; OnPropertyChanged(); }
    }

    public bool HasDevices => Devices.Count > 0;

    public bool IsDeviceEmpty => !HasDevices;

    public string TaktStatus => GetText("Navigation_Dashboard_TaktEmpty", "当前没有稳定实时节拍来源。");

    public string TrendStatus => GetText("Navigation_Dashboard_TrendEmpty", "当前没有稳定趋势数据来源。");

    public override async Task OnActivatedAsync()
    {
        await RunViewTaskAsync(LoadDashboardAsync, GetText("Navigation_Dashboard_LoadFailed", "加载首页总览失败。"));
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        RefreshLanguageProperties();
    }

    private async Task LoadDashboardAsync()
    {
        var hardwareTask = _equipmentPanelService.GetHardwareStatusAsync();
        var recipeTask = _equipmentPanelService.GetRecipeSnapshotAsync();
        var capacityTask = _equipmentPanelService.GetCapacitySnapshotAsync();

        await Task.WhenAll(hardwareTask, recipeTask, capacityTask);

        var hardware = await hardwareTask;
        var recipe = await recipeTask;
        var capacity = await capacityTask;

        await AvaloniaDispatcher.UIThread.InvokeAsync(() =>
        {
            ReplaceItems(
                Devices,
                hardware.Select(snapshot => new DashboardDeviceItemViewModel
                {
                    Name = snapshot.Name,
                    Address = snapshot.Address,
                    DeviceType = snapshot.DeviceType,
                    IsConnected = snapshot.IsConnected
                }));

            var connected = hardware.Count(x => x.IsConnected);
            ConnectedDevices = $"{connected} / {hardware.Count}";

            RecipeName = recipe?.RecipeName ?? "--";
            SetRecipeStatus(
                recipe is null
                    ? "Navigation_Dashboard_RecipeMissing"
                    : recipe.IsRecipeActive
                        ? "Navigation_Dashboard_RecipeActive"
                        : "Navigation_Dashboard_RecipeInactive",
                recipe is null
                    ? "未加载配方"
                    : recipe.IsRecipeActive
                        ? "已激活"
                        : "未激活");

            TodayOutput = capacity.TodayOutput.ToString();
            TodayYield = capacity.TodayYield;
            NgCount = capacity.NgCount.ToString();
            CurrentBatch = capacity.CurrentBatch;

            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(IsDeviceEmpty));
        });
    }

    private void RefreshLanguageProperties()
    {
        OnPropertyChanged(nameof(TaktStatus));
        OnPropertyChanged(nameof(TrendStatus));
        OnPropertyChanged(nameof(RecipeStatus));
    }

    private void SetRecipeStatus(string resourceKey, string fallback)
    {
        _recipeStatusResourceKey = resourceKey;
        _recipeStatusFallback = fallback;
        OnPropertyChanged(nameof(RecipeStatus));
    }
}
