using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Production.DataView;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed partial class DataViewModel : NavigationPageViewModelBase
{
    private readonly IDataViewService _dataViewService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly EdgeRuntimePaths _runtimePaths;
    private readonly IAvaloniaCsvExportService _csvExportService;

    public DataViewModel(
        IDataViewService dataViewService,
        IAvaloniaLanguageService languageService,
        EdgeRuntimePaths runtimePaths,
        IAvaloniaCsvExportService csvExportService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _dataViewService = dataViewService;
        _languageService = languageService;
        _runtimePaths = runtimePaths;
        _csvExportService = csvExportService;
    }

    public DataViewModel(
        IDataViewService dataViewService,
        IAvaloniaLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : this(
            dataViewService,
            languageService,
            CreateDefaultRuntimePaths(),
            new AvaloniaCsvExportService(),
            viewId,
            titleResourceKey,
            titleFallback)
    {
    }

    public ObservableCollection<ProductionRecordRow> Records { get; } = [];

    [ObservableProperty]
    private DateTime dateFrom = DateTime.Today;

    [ObservableProperty]
    private DateTime dateTo = DateTime.Today;

    [ObservableProperty]
    private int todayTotal;

    [ObservableProperty]
    private int todayOk;

    [ObservableProperty]
    private int todayNg;

    [ObservableProperty]
    private string todayYield = "0.00%";

    [ObservableProperty]
    private string feedbackMessage = string.Empty;

    public override Task OnActivatedAsync()
        => QueryAsync();

    [RelayCommand]
    private async Task QueryAsync()
    {
        try
        {
            var snapshot = await _dataViewService.QueryAsync(DateFrom.Date, DateTo.Date);
            TodayTotal = snapshot.TodayTotal;
            TodayOk = snapshot.TodayOk;
            TodayNg = snapshot.TodayNg;
            TodayYield = snapshot.TodayYield;

            Records.Clear();
            foreach (var record in snapshot.Records)
            {
                Records.Add(new ProductionRecordRow(
                    record.Time,
                    record.BatchNo,
                    record.Total,
                    record.OkCount,
                    record.NgCount,
                    record.Yield));
            }

            FeedbackMessage = Records.Count == 0
                ? Text("Navigation_Monitor_NoDeviceData", "暂无数据")
                : string.Empty;
        }
        catch (Exception ex)
        {
            FeedbackMessage = $"{Text("Navigation_Data_QueryFailed", "生产数据查询失败。")}{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        try
        {
            var path = await _csvExportService.ExportAsync(
                _runtimePaths.ExcelDirectory,
                "DataView",
                ["时间", "批次", "总数", "OK", "NG", "良率"],
                Records.Select(static row => new object?[]
                {
                    row.Time,
                    row.BatchNo,
                    row.Total,
                    row.Ok,
                    row.Ng,
                    row.Yield
                }),
                DateTime.Now);
            FeedbackMessage = $"已导出：{path}";
        }
        catch (Exception ex)
        {
            FeedbackMessage = $"导出生产数据失败：{ex.Message}";
        }
    }

    private string Text(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static EdgeRuntimePaths CreateDefaultRuntimePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "iiot-edge-avalonia-export");
        return new EdgeRuntimePaths(
            root,
            "avalonia-tests",
            root,
            Path.Combine(root, "db"),
            Path.Combine(root, "context"),
            Path.Combine(root, "recipes"),
            Path.Combine(root, "excel"),
            Path.Combine(root, "diagnostics"),
            Path.Combine(root, "logs"),
            Path.Combine(root, "diagnostics", "device-cache.json"),
            Path.Combine(root, "logs", "crash.log"),
            Path.Combine(root, "logs", "crash-fallback.log"));
    }
}

public sealed record ProductionRecordRow(string Time, string BatchNo, int Total, int Ok, int Ng, string Yield);
