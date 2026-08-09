using System.Collections.ObjectModel;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
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
    private string _okCount = "0";
    private string _ngCount = "0";
    private string _currentBatch = "--";
    private string _recentHourOutput = "0";
    private string _recentHourOk = "0";
    private string _recentHourNg = "0";
    private string _recentHourLabel = "--";

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
        private set
        {
            _recipeName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProductionSummaryItems));
        }
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
        private set
        {
            _ngCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(YieldSummaryItems));
            OnPropertyChanged(nameof(OutputSummaryItems));
        }
    }

    public string OkCount
    {
        get => _okCount;
        private set
        {
            _okCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(YieldSummaryItems));
        }
    }

    public string CurrentBatch
    {
        get => _currentBatch;
        private set
        {
            _currentBatch = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProductionSummaryItems));
        }
    }

    public string RecentHourOutput
    {
        get => _recentHourOutput;
        private set
        {
            _recentHourOutput = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputSummaryItems));
        }
    }

    public string RecentHourOk
    {
        get => _recentHourOk;
        private set
        {
            _recentHourOk = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputSummaryItems));
        }
    }

    public string RecentHourNg
    {
        get => _recentHourNg;
        private set
        {
            _recentHourNg = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputSummaryItems));
        }
    }

    public string RecentHourLabel
    {
        get => _recentHourLabel;
        private set
        {
            _recentHourLabel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputSummaryItems));
        }
    }

    public IReadOnlyList<EdgeSummaryItem> OutputSummaryItems =>
    [
        new()
        {
            Label = GetText("Navigation_DashboardPreview_RecentHourOutput", "最近1小时产能"),
            Value = NormalizeSummaryValue(RecentHourOutput)
        },
        new()
        {
            Label = GetText("Navigation_Label_GoodCount", "良品数"),
            Value = NormalizeSummaryValue(RecentHourOk)
        },
        new()
        {
            Label = GetText("Navigation_Label_BadCount", "不良数"),
            Value = NormalizeSummaryValue(RecentHourNg)
        }
    ];

    public IReadOnlyList<EdgeSummaryItem> YieldSummaryItems =>
    [
        new()
        {
            Label = GetText("Navigation_Label_GoodCount", "良品数"),
            Value = NormalizeSummaryValue(OkCount)
        },
        new()
        {
            Label = GetText("Navigation_Label_BadCount", "不良数"),
            Value = NormalizeSummaryValue(NgCount)
        }
    ];

    public IReadOnlyList<EdgeSummaryItem> ProductionSummaryItems =>
    [
        new()
        {
            Label = GetText("Navigation_DashboardPreview_CurrentBatch", "当前批次"),
            Value = NormalizeSummaryValue(CurrentBatch)
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_Recipe", "配方"),
            Value = NormalizeSummaryValue(RecipeName)
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_RecipeStatus", "配方状态"),
            Value = NormalizeSummaryValue(RecipeStatus)
        }
    ];

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

            TodayOutput = capacity.IsAvailable ? capacity.TodayOutput.ToString() : "--";
            TodayYield = capacity.IsAvailable ? capacity.TodayYield : "--";
            OkCount = capacity.IsAvailable ? capacity.OkCount.ToString() : "--";
            NgCount = capacity.IsAvailable ? capacity.NgCount.ToString() : "--";
            CurrentBatch = capacity.IsAvailable ? capacity.CurrentBatch ?? "--" : "--";
            RecentHourOutput = capacity.IsAvailable ? capacity.RecentHourOutput.ToString() : "--";
            RecentHourOk = capacity.IsAvailable ? capacity.RecentHourOk.ToString() : "--";
            RecentHourNg = capacity.IsAvailable ? capacity.RecentHourNg.ToString() : "--";
            RecentHourLabel = capacity.RecentHourLabel;

            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(IsDeviceEmpty));
            OnPropertyChanged(nameof(ProductionSummaryItems));
            OnPropertyChanged(nameof(OutputSummaryItems));
            OnPropertyChanged(nameof(YieldSummaryItems));
        });
    }

    private void RefreshLanguageProperties()
    {
        OnPropertyChanged(nameof(TaktStatus));
        OnPropertyChanged(nameof(TrendStatus));
        OnPropertyChanged(nameof(RecipeStatus));
        OnPropertyChanged(nameof(ProductionSummaryItems));
        OnPropertyChanged(nameof(OutputSummaryItems));
        OnPropertyChanged(nameof(YieldSummaryItems));
    }

    private void SetRecipeStatus(string resourceKey, string fallback)
    {
        _recipeStatusResourceKey = resourceKey;
        _recipeStatusFallback = fallback;
        OnPropertyChanged(nameof(RecipeStatus));
        OnPropertyChanged(nameof(ProductionSummaryItems));
    }

    private static string NormalizeSummaryValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value == "--"
            ? "—"
            : value;
    }
}
