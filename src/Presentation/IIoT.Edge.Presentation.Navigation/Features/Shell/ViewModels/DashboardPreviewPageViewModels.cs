using System.ComponentModel;
using IIoT.Edge.Presentation.Navigation.Features.Dashboard;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

internal sealed class DashboardPreviewRuntimeViewModel : BaseNotifyPropertyChanged, IDisposable
{
    private const string EmptyValue = "—";

    private readonly DashboardViewModel _source;
    private readonly IAppLanguageService _languageService;

    public DashboardPreviewRuntimeViewModel(DashboardViewModel source, IAppLanguageService languageService)
    {
        _source = source;
        _languageService = languageService;
        _source.PropertyChanged += OnSourcePropertyChanged;
        _languageService.LanguageChanged += OnLanguageChanged;
    }

    public string TodayOutput => Normalize(_source.TodayOutput);

    public string TodayYield => Normalize(_source.TodayYield);

    public string ConnectedDevices => Normalize(_source.ConnectedDevices);

    public string NgCount => Normalize(_source.NgCount);

    public string OkCount => EmptyValue;

    public string CurrentBatch => Normalize(_source.CurrentBatch);

    public IReadOnlyList<EdgeSummaryItem> ProductionSummaryItems =>
    [
        new()
        {
            Label = GetText("Navigation_DashboardPreview_CurrentProgram", "当前程序"),
            Value = EmptyValue
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_CurrentBatch", "当前批次"),
            Value = CurrentBatch
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_Recipe", "配方"),
            Value = Normalize(_source.RecipeName)
        }
    ];

    public IReadOnlyList<DashboardPreviewStatusItem> RuntimeStatusItems =>
    [
        new(
            GetText("Navigation_DashboardPreview_DeviceLinks", "设备连接"),
            ConnectedDevices,
            GetText("Navigation_DashboardPreview_DeviceLinkDescription", "硬件服务返回的当前连接数"),
            ResolveHardwareStatus(),
            ResolveHardwareStatusText()),
        new(
            GetText("Navigation_DashboardPreview_RecipeStatus", "配方状态"),
            Normalize(_source.RecipeStatus),
            GetText("Navigation_DashboardPreview_FromCapacitySnapshot", "来自当前产能快照"),
            ResolveRecipeStatus(),
            Normalize(_source.RecipeStatus))
    ];

    public IReadOnlyList<DashboardPreviewAlertItem> AlertItems => [];

    public bool IsAlertEmpty => true;

    public IReadOnlyList<DashboardPreviewTrendPoint> TrendPoints => [];

    public bool IsTrendEmpty => true;

    public Task OnActivatedAsync() => _source.OnActivatedAsync();

    public Task OnDeactivatedAsync() => _source.OnDeactivatedAsync();

    public void Dispose()
    {
        _source.PropertyChanged -= OnSourcePropertyChanged;
        _languageService.LanguageChanged -= OnLanguageChanged;
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(string.Empty);

    private void OnLanguageChanged(object? sender, EventArgs e)
        => OnPropertyChanged(string.Empty);

    private EdgeVisualStatus ResolveHardwareStatus()
        => ConnectedDevices.StartsWith("0 /", StringComparison.Ordinal) ? EdgeVisualStatus.Offline : EdgeVisualStatus.Running;

    private string ResolveHardwareStatusText()
        => ResolveHardwareStatus() == EdgeVisualStatus.Running
            ? GetText("Navigation_DashboardPreview_StatusRunning", "运行中")
            : GetText("Navigation_DashboardPreview_StatusInactive", "未激活");

    private EdgeVisualStatus ResolveRecipeStatus()
        => string.Equals(Normalize(_source.RecipeStatus), EmptyValue, StringComparison.Ordinal)
            ? EdgeVisualStatus.Offline
            : EdgeVisualStatus.Info;

    private string GetText(string key, string fallback)
        => _languageService.GetString(key, fallback);

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) || value == "--" ? EmptyValue : value;
}

internal sealed class DashboardPreviewDesignViewModel : BaseNotifyPropertyChanged, IDisposable
{
    private readonly IAppLanguageService _languageService;

    public DashboardPreviewDesignViewModel(IAppLanguageService languageService)
    {
        _languageService = languageService;
        _languageService.LanguageChanged += OnLanguageChanged;
    }

    public string TodayOutput => "12,860";

    public string TodayYield => "98.6%";

    public string ConnectedDevices => "8 / 8";

    public string NgCount => "12";

    public string OkCount => "12,848";

    public string CurrentBatch => "B20260521-01";

    public IReadOnlyList<EdgeSummaryItem> ProductionSummaryItems =>
    [
        new()
        {
            Label = GetText("Navigation_DashboardPreview_CurrentProgram", "当前程序"),
            Value = "Homogenization-A"
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_CurrentBatch", "当前批次"),
            Value = CurrentBatch
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_Recipe", "配方"),
            Value = "浆料配方 V2.3"
        }
    ];

    public IReadOnlyList<DashboardPreviewStatusItem> RuntimeStatusItems =>
    [
        new(
            GetText("Navigation_DashboardPreview_StatusRunning", "运行中"),
            "3",
            GetText("Navigation_DashboardPreview_RuntimeState", "设备状态"),
            EdgeVisualStatus.Running,
            GetText("Navigation_DashboardPreview_StatusRunning", "运行中")),
        new(
            GetText("Navigation_DashboardPreview_StatusIdle", "待机"),
            "2",
            GetText("Navigation_DashboardPreview_RuntimeState", "设备状态"),
            EdgeVisualStatus.Idle,
            GetText("Navigation_DashboardPreview_StatusIdle", "待机")),
        new(
            GetText("Navigation_DashboardPreview_StatusInactive", "未激活"),
            "1",
            GetText("Navigation_DashboardPreview_RuntimeState", "设备状态"),
            EdgeVisualStatus.Offline,
            GetText("Navigation_DashboardPreview_StatusInactive", "未激活")),
        new(
            GetText("Navigation_DashboardPreview_StatusError", "异常"),
            "0",
            GetText("Navigation_DashboardPreview_RuntimeState", "设备状态"),
            EdgeVisualStatus.Error,
            GetText("Navigation_DashboardPreview_StatusError", "异常"))
    ];

    public IReadOnlyList<DashboardPreviewAlertItem> AlertItems =>
    [
        new("09:24", "WARN", "真空度波动接近预警阈值", EdgeVisualStatus.Warning),
        new("09:18", "INFO", "批次 B20260521-01 已完成一次出料", EdgeVisualStatus.Info),
        new("09:02", "INFO", "MES 批次上下文已同步到本地", EdgeVisualStatus.Info)
    ];

    public bool IsAlertEmpty => false;

    public IReadOnlyList<DashboardPreviewTrendPoint> TrendPoints =>
    [
        new("08:00", 52, "1,920"),
        new("09:00", 68, "2,480"),
        new("10:00", 61, "2,240"),
        new("11:00", 74, "2,720"),
        new("12:00", 57, "2,080")
    ];

    public bool IsTrendEmpty => false;

    public void Dispose()
        => _languageService.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e)
        => OnPropertyChanged(string.Empty);

    private string GetText(string key, string fallback)
        => _languageService.GetString(key, fallback);
}

internal sealed record DashboardPreviewStatusItem(
    string Title,
    string Description,
    string Detail,
    EdgeVisualStatus Status,
    string StatusText);

internal sealed record DashboardPreviewAlertItem(
    string Time,
    string Level,
    string Message,
    EdgeVisualStatus Status);

internal sealed record DashboardPreviewTrendPoint(
    string Label,
    double BarHeight,
    string Value);
