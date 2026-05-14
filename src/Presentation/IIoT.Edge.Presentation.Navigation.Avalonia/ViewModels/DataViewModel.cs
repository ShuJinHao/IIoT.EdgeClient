using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Features.Production.DataView;
using IIoT.Edge.UI.Avalonia.Localization;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed partial class DataViewModel : NavigationPageViewModelBase
{
    private readonly IDataViewService _dataViewService;
    private readonly IAvaloniaLanguageService _languageService;

    public DataViewModel(
        IDataViewService dataViewService,
        IAvaloniaLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _dataViewService = dataViewService;
        _languageService = languageService;
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
    private void Export()
    {
        FeedbackMessage = "当前 Avalonia 迁移批次不写出导出文件。";
    }

    private string Text(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}

public sealed record ProductionRecordRow(string Time, string BatchNo, int Total, int Ok, int Ng, string Yield);
