using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Resources;
using IIoT.Edge.Module.Homogenization.Production;
using IIoT.Edge.Presentation.Navigation.PluginSystem;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.PluginSystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Presentation;

public sealed class HomogenizationDataViewModel : PresentationViewModelBase
{
    private const string EmptyValue = "-";
    private static readonly string[] ColumnHeaderPropertyNames =
    [
        nameof(TrayCodeHeader),
        nameof(DeviceCodeHeader),
        nameof(DeviceNameHeader),
        nameof(InboundTimeHeader),
        nameof(OutboundTimeHeader),
        nameof(StatusHeader),
        nameof(StirringSpeedHeader),
        nameof(TemperatureHeader),
        nameof(VacuumHeader),
        nameof(CntActualHeader),
        nameof(CntTargetHeader),
        nameof(CntTankAWeightHeader),
        nameof(CntTankBWeightHeader),
        nameof(NmpActualHeader),
        nameof(NmpTargetHeader),
        nameof(GlueActualHeader),
        nameof(SetStirringTimeHeader),
        nameof(RemainingStirringTimeHeader),
        nameof(SetDispersionTimeHeader),
        nameof(RemainingDispersionTimeHeader),
        nameof(MainBatchPlanHeader),
        nameof(BatchNumberHeader)
    ];

    private readonly IProductionContextStore _contextStore;
    private readonly IAppLanguageService _languageService;
    private readonly bool _visualTestDataEnabled;
    private readonly string _visualTestBatchCode;
    private readonly DispatcherTimer _timer;

    public HomogenizationDataViewModel(
        IProductionContextStore contextStore,
        IAppLanguageService languageService,
        IConfiguration configuration,
        IOptions<HomogenizationModuleOptions> moduleOptions)
    {
        _contextStore = contextStore;
        _languageService = languageService;
        _visualTestDataEnabled = configuration.GetValue("UI:VisualTestData:Enabled", false);
        _visualTestBatchCode = configuration["UI:VisualTestData:BatchCode"] ?? "VT-HG-20260602-01";
        _timer = HomogenizationPresentationHelpers.CreateTimer(
            RefreshAsync,
            moduleOptions.Value.Presentation.DataViewRefreshIntervalMs);
        _languageService.LanguageChanged += (_, _) => RefreshLocalizedText();
    }

    public override string ViewId => StandardModuleViewIds.Create(DependencyInjection.ModuleKey).DataView;

    public override string ViewTitle => HomogenizationText.Get("Homogenization_Title_Data", "匀浆出料数据");

    public ObservableCollection<HomogenizationDataRow> Records { get; } = [];

    public bool IsRecordsEmpty => Records.Count == 0;

    public bool HasRecords => Records.Count > 0;

    public string EmptyTitle => HomogenizationText.Get("Homogenization_Empty_Title", "暂无出料记录");

    public string EmptyMessage => HomogenizationText.Get("Homogenization_Empty_OutboundRecords", "暂无匀浆出料记录。");

    public string TrayCodeHeader => GetText("Homogenization_Column_TrayCode", "托盘码");

    public string DeviceCodeHeader => GetText("Homogenization_Column_DeviceCode", "设备编码");

    public string DeviceNameHeader => GetText("Homogenization_Column_DeviceName", "设备名称");

    public string InboundTimeHeader => GetText("Homogenization_Column_InboundTime", "进站时间");

    public string OutboundTimeHeader => GetText("Homogenization_Column_OutboundTime", "出料时间");

    public string StatusHeader => GetText("Homogenization_Column_Status", "运行状态");

    public string StirringSpeedHeader => GetText("Homogenization_Column_StirringSpeed", "搅拌转速(RPM)");

    public string TemperatureHeader => GetText("Homogenization_Column_Temperature", "温度(C)");

    public string VacuumHeader => GetText("Homogenization_Column_Vacuum", "真空度(KPa)");

    public string CntActualHeader => GetText("Homogenization_Column_CntActual", "CNT实际值(kg)");

    public string CntTargetHeader => GetText("Homogenization_Column_CntTarget", "CNT目标值(kg)");

    public string CntTankAWeightHeader => GetText("Homogenization_Column_CntTankAWeight", "CNT A罐重量(kg)");

    public string CntTankBWeightHeader => GetText("Homogenization_Column_CntTankBWeight", "CNT B罐重量(kg)");

    public string NmpActualHeader => GetText("Homogenization_Column_NmpActual", "NMP实际值(kg)");

    public string NmpTargetHeader => GetText("Homogenization_Column_NmpTarget", "NMP目标值(kg)");

    public string GlueActualHeader => GetText("Homogenization_Column_GlueActual", "胶液实际重量(kg)");

    public string SetStirringTimeHeader => GetText("Homogenization_Column_SetStirringTime", "设定搅拌时间(min)");

    public string RemainingStirringTimeHeader => GetText("Homogenization_Column_RemainingStirringTime", "剩余搅拌时间(min)");

    public string SetDispersionTimeHeader => GetText("Homogenization_Column_SetDispersionTime", "设定分散时间(min)");

    public string RemainingDispersionTimeHeader => GetText("Homogenization_Column_RemainingDispersionTime", "剩余分散时间(min)");

    public string MainBatchPlanHeader => GetText("Homogenization_Column_MainBatchPlan", "主批计划");

    public string BatchNumberHeader => GetText("Homogenization_Column_BatchNumber", "追溯批次号");

    public override Task OnActivatedAsync()
    {
        _timer.Start();
        return RefreshAsync();
    }

    public override Task OnDeactivatedAsync()
    {
        _timer.Stop();
        return Task.CompletedTask;
    }

    private Task RefreshAsync()
        => RunViewTaskAsync(() =>
        {
            var rows = _visualTestDataEnabled
                ? BuildVisualTestRows(_visualTestBatchCode)
                : LoadContextRows();

            ReplaceItems(Records, rows);
            OnPropertyChanged(nameof(IsRecordsEmpty));
            OnPropertyChanged(nameof(HasRecords));
            SetStatus(rows.Length == 0
                ? EmptyMessage
                : HomogenizationText.Format("Homogenization_RecordCountFormat", "共 {0} 条出料记录。", rows.Length));
            return Task.CompletedTask;
        }, trackBusy: false, clearFeedback: false);

    private HomogenizationDataRow[] LoadContextRows()
        => _contextStore.GetAll()
            .OfType<HomogenizationContext>()
            .SelectMany(static x => x.OutboundRecords)
            .OrderByDescending(static x => x.CompletedTime ?? x.InboundTime ?? DateTime.MinValue)
            .Select(static x => new HomogenizationDataRow(
                FormatText(x.TrayCode),
                FormatText(x.DeviceCode),
                FormatText(x.DeviceName),
                FormatDate(x.InboundTime),
                FormatDate(x.CompletedTime),
                FormatText(x.RuntimeStatus),
                x.RealtimeSnapshot?.StirringSpeed.ToString(CultureInfo.InvariantCulture) ?? EmptyValue,
                x.RealtimeSnapshot?.Temperature.ToString(CultureInfo.InvariantCulture) ?? EmptyValue,
                x.RealtimeSnapshot?.Vacuum.ToString(CultureInfo.InvariantCulture) ?? EmptyValue,
                FormatNumber(x.CntActualKg),
                FormatNumber(x.CntTargetKg),
                FormatNumber(x.CntTankAWeightKg),
                FormatNumber(x.CntTankBWeightKg),
                FormatNumber(x.NmpActualKg),
                FormatNumber(x.NmpTargetKg),
                FormatNumber(x.GlueActualKg),
                FormatNumber(x.SetStirringTimeMinutes),
                FormatNumber(x.RemainingStirringTimeMinutes),
                FormatNumber(x.SetDispersionTimeMinutes),
                FormatNumber(x.RemainingDispersionTimeMinutes),
                FormatText(x.MainBatchPlan),
                FormatText(x.BatchNumber)))
            .ToArray();

    private static HomogenizationDataRow[] BuildVisualTestRows(string batchCode)
    {
        var baseTime = DateTime.Now.Date.AddHours(8);
        const string mainPlanCode = "MES-HG-MAIN-20260604-A";
        return Enumerable.Range(0, 18)
            .Select(index =>
            {
                var inboundTime = baseTime.AddMinutes(index * 18);
                var completedTime = inboundTime.AddMinutes(42 + index % 4 * 3);
                var trayIndex = index + 1;
                var stirringSpeed = 610 + index % 7 * 4;
                var temperature = 41.8 + index % 6 * 0.3;
                var vacuum = -88.0 - index % 5 * 0.4;
                var cntActual = 120.0 + index % 8 * 0.7;
                var nmpActual = 82.0 + index % 6 * 0.5;
                var glueActual = 56.0 + index % 5 * 0.4;
                var status = index % 8 == 0
                    ? "待复核"
                    : index % 5 == 0
                        ? "混料中"
                        : "已出料";

                return new HomogenizationDataRow(
                    $"TR-HG-01-{trayIndex:D3}",
                    "PLC-HG-A01",
                    "匀浆 A 线 PLC",
                    FormatDate(inboundTime),
                    FormatDate(completedTime),
                    status,
                    stirringSpeed.ToString(CultureInfo.InvariantCulture),
                    temperature.ToString("0.0", CultureInfo.InvariantCulture),
                    vacuum.ToString("0.0", CultureInfo.InvariantCulture),
                    cntActual.ToString("0.0", CultureInfo.InvariantCulture),
                    "128.0",
                    (63.0 + index % 3).ToString("0.0", CultureInfo.InvariantCulture),
                    (66.0 + index % 4).ToString("0.0", CultureInfo.InvariantCulture),
                    nmpActual.ToString("0.0", CultureInfo.InvariantCulture),
                    "88.0",
                    glueActual.ToString("0.0", CultureInfo.InvariantCulture),
                    "45",
                    Math.Max(0, 45 - index % 9 * 4).ToString(CultureInfo.InvariantCulture),
                    "30",
                    Math.Max(0, 30 - index % 6 * 5).ToString(CultureInfo.InvariantCulture),
                    mainPlanCode,
                    $"{batchCode}-{trayIndex:D2}");
            })
            .ToArray();
    }

    private void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(ViewTitle));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
        foreach (var propertyName in ColumnHeaderPropertyNames)
        {
            OnPropertyChanged(propertyName);
        }

        SetStatus(IsRecordsEmpty
            ? EmptyMessage
            : HomogenizationText.Format("Homogenization_RecordCountFormat", "共 {0} 条出料记录。", Records.Count));
    }

    private static string GetText(string key, string fallback)
        => HomogenizationText.Get(key, fallback);

    private static string FormatText(string? value)
        => string.IsNullOrWhiteSpace(value) ? EmptyValue : value.Trim();

    private static string FormatDate(DateTime? value)
        => value?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? EmptyValue;

    private static string FormatNumber(double? value)
        => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? EmptyValue;

    private static string FormatNumber(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? EmptyValue;
}

public sealed record HomogenizationDataRow(
    string TrayCode,
    string DeviceCode,
    string DeviceName,
    string InboundTime,
    string OutboundTime,
    string Status,
    string StirringSpeed,
    string Temperature,
    string Vacuum,
    string CntActual,
    string CntTarget,
    string CntTankAWeight,
    string CntTankBWeight,
    string NmpActual,
    string NmpTarget,
    string GlueActual,
    string SetStirringTime,
    string RemainingStirringTime,
    string SetDispersionTime,
    string RemainingDispersionTime,
    string MainBatchPlan,
    string BatchNumber);

internal static class HomogenizationPresentationHelpers
{
    public static DispatcherTimer CreateTimer(Func<Task> refreshAsync, int intervalMs)
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(200, intervalMs))
        };
        timer.Tick += async (_, _) => await refreshAsync();
        return timer;
    }
}
